// BarberLoc map JS moved out of the Razor view to avoid Razor parsing issues.
// The Google Maps API script must be included in the view before this file.

// Ensure geometry library is present when using computeDistanceBetween
if (typeof google === 'undefined' || !google.maps) {
    console.warn('Google Maps API not loaded before barbershops-map.js');
}

// main variables
let map;
let infowindow;
let placesService;
let placesMarkers = [];
let placesIndex = {};
let activePlaceId = null;
let serverMarkers = [];
let userMarker = null;
let accuracyCircle = null;
let geoWatchId = null;
let hasUserInteracted = false;

function initMap() {
    const portugal = { lat: 41.3, lng: -6.7 };
    map = new google.maps.Map(document.getElementById("map"), {
        zoom: 12,
        center: portugal,
        mapTypeControl: true,
        streetViewControl: true,
        fullscreenControl: true,
        zoomControl: true
    });

    try {
        map.setOptions({
            styles: [
                { featureType: 'poi.business', elementType: 'labels.icon', stylers: [{ visibility: 'off' }] },
                { featureType: 'transit', elementType: 'labels.icon', stylers: [{ visibility: 'off' }] }
            ]
        });
    } catch (e) {
        console.warn('Failed to apply map styles to hide POI icons', e);
    }

    map.addListener('dragstart', () => { hasUserInteracted = true; });
    map.addListener('zoom_changed', () => { hasUserInteracted = true; });

    infowindow = new google.maps.InfoWindow();
    placesService = new google.maps.places.PlacesService(map);
    infowindow.addListener('closeclick', () => { activePlaceId = null; });

    let idleTimer;
    let lastCenter = map.getCenter();
    let lastZoom = map.getZoom();
    map.addListener('idle', () => {
        clearTimeout(idleTimer);
        idleTimer = setTimeout(() => {
            const c = map.getCenter();
            const z = map.getZoom();
            const dist = lastCenter ? google.maps.geometry.spherical.computeDistanceBetween(lastCenter, c) : 9999;
            if (!lastCenter || dist > 50 || Math.abs(z - lastZoom) >= 1) {
                lastCenter = c;
                lastZoom = z;
                try { searchPlacesInBounds(); } catch (e) { console.warn('searchPlacesInBounds failed', e); const dbg = document.getElementById('debugContent'); if (dbg) { dbg.textContent = 'Search failed: ' + (e.message || e); document.getElementById('debugPanel').style.display = 'block'; } }
            }
        }, 600);
    });

    setupUIListeners();
    searchPlacesInBounds();
    if (navigator && navigator.geolocation) { locateUser(); }

    map.addListener('click', (event) => {
        if (event.placeId) {
            event.stop();
            showGooglePOIInfo(event.placeId, event.latLng);
        }
    });
}

function clearMarkers() {
    placesMarkers.forEach(m => m.setMap(null));
    placesMarkers = [];
    placesIndex = {};
    serverMarkers.forEach(s => { if (s.marker) s.marker.setMap(null); });
    serverMarkers = [];
}

async function searchPlacesInBounds() {
    const loading = document.getElementById('mapLoading'); if (loading) loading.style.display = 'block';
    const filterBarber = document.getElementById('filterBarber')?.checked;
    const filterSalon = document.getElementById('filterSalon')?.checked;
    const categories = [];
    if (filterBarber) categories.push('Barbershop');
    if (filterSalon) categories.push('HairSalon');

    clearMarkers();

    try {
        let url = '/Barbershops/GetMapData?categories=' + encodeURIComponent(categories.join(','));
        if (userMarker && userMarker.getPosition) {
            const pos = userMarker.getPosition();
            url += '&lat=' + pos.lat() + '&lng=' + pos.lng() + '&radiusKm=10';
        } else {
            url += '&minRating=4.0';
        }

        const resp = await fetch(url + '&genders=' + encodeURIComponent(getSelectedGenders()) + '&mobileOnly=' + (document.getElementById('mobileOnly') ? document.getElementById('mobileOnly').checked : false), { credentials: 'same-origin' });
        if (!resp.ok) {
            const dbg = document.getElementById('debugContent');
            if (resp.status === 401 || resp.status === 403) { if (dbg) dbg.textContent = 'Os dados do mapa requerem autenticação. Faça login para ver as barbearias.'; }
            else { if (dbg) dbg.textContent = 'Erro ao obter dados do servidor para o mapa. Código: ' + resp.status; }
            const dp = document.getElementById('debugPanel'); if (dp) dp.style.display = 'block';
            showMapAuthBanner(resp.status);
            return;
        }

        const contentType = (resp.headers.get('content-type') || '').toLowerCase();
        let places = [];
        try { if (contentType.includes('application/json')) { places = await resp.json(); } else { const txt = await resp.text(); const dbg = document.getElementById('debugContent'); if (dbg) dbg.textContent = 'Resposta do servidor inesperada (não JSON). Possível redirecionamento para login.' + (txt ? '\n' + txt.substring(0,300) : ''); const dp = document.getElementById('debugPanel'); if (dp) dp.style.display = 'block'; showMapAuthBanner(); places = []; } } catch (ex) { console.error('Failed to parse GetMapData response', ex); const dbg = document.getElementById('debugContent'); if (dbg) dbg.textContent = 'Erro ao analisar resposta do servidor: ' + (ex.message || ex); const dp = document.getElementById('debugPanel'); if (dp) dp.style.display = 'block'; places = []; }

        const bounds = new google.maps.LatLngBounds();
        places.forEach(p => {
            if (typeof p.lat !== 'number' || typeof p.lng !== 'number') return;
            const pos = new google.maps.LatLng(p.lat, p.lng);
            const marker = new google.maps.Marker({ position: pos, map: map, title: p.name });
            marker.addListener('click', () => { showServerPlaceInfo(p, marker); renderPlaceDetailsPanel(p); });
            serverMarkers.push({ marker: marker, data: p });
            bounds.extend(pos);
        });

        if (activePlaceId) {
            const reopened = serverMarkers.find(s => s.data && s.data.id == activePlaceId);
            if (reopened) { try { showServerPlaceInfo(reopened.data, reopened.marker); } catch (e) { console.warn('Failed to re-open active place info', e); } } else { activePlaceId = null; }
        }

        if (!bounds.isEmpty()) {
            try {
                if (places.length > 1 && !hasUserInteracted) {
                    if (!(userMarker && userMarker.getPosition())) { map.fitBounds(bounds); }
                }
            } catch (e) { console.warn(e); }
        }

        updateSidebar();
    } catch (ex) { console.error(ex); } finally { if (loading) loading.style.display = 'none'; }
}

function showServerPlaceInfo(place, marker) {
    try { activePlaceId = place.id || null; } catch (e) { activePlaceId = null; }

    const renderWithData = () => {
        const panoId = 'svPanorama_' + Math.random().toString(36).substr(2, 9);
        const mapsUrl = place.placeId ? ('https://www.google.com/maps/place/?q=place_id:' + encodeURIComponent(place.placeId)) : ('https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(place.name + ' ' + place.address));

        function buildContent(detailsInfo, reviewsHtml) {
            const phoneHtml = detailsInfo && detailsInfo.formatted_phone_number ? `<div class="mb-1"><a href=\"tel:${detailsInfo.formatted_phone_number}\" class="small">${detailsInfo.formatted_phone_number}</a></div>` : '';
            const hoursHtml = detailsInfo && detailsInfo.opening_hours && detailsInfo.opening_hours.weekday_text ? `<div class="small text-muted mb-1">${detailsInfo.opening_hours.weekday_text.slice(0,3).join(' / ')}</div>` : '';
            const websiteHtml = detailsInfo && detailsInfo.website ? `<div class="mt-1"><a target=\"_blank\" href=\"${detailsInfo.website}\" class="small">Visitar site</a></div>` : '';
            const photoHtml = detailsInfo && detailsInfo.photos && detailsInfo.photos.length ? `<div class="mb-2 text-center"><img src=\"${detailsInfo.photos[0]}\" alt=\"${place.name}\" style=\"max-width:100%; max-height:120px; object-fit:cover;\"/></div>` : (place.image ? `<div class="mb-2 text-center"><img src=\"${place.image}\" alt=\"${place.name}\" style=\"max-width:100%; max-height:120px; object-fit:cover;\"/></div>` : '');

            const content = `
                        <div class="p-2 text-start">
                            ${photoHtml}
                            <h6 class="mb-1">${place.name}</h6>
                            <p class="small text-muted mb-2">${place.address || ''}</p>
                            <div class="mb-2">${(typeof place.rating === 'number' && place.rating>0) ? `<span class=\"badge bg-warning text-dark\">${place.rating.toFixed(1)} <i class=\"fas fa-star\"></i></span>` : ''}</div>
                            ${phoneHtml}
                            ${hoursHtml}
                            ${websiteHtml}
                            <a href="/Barbershops/Details/${place.id}" class="btn btn-sm btn-primary mt-1">Ver detalhes</a>
                            <div id="${panoId}" class="mt-2" style="width:100%; height:140px; background:#f5f5f5; display:flex; align-items:center; justify-content:center;">
                                <a target="_blank" href="${mapsUrl}" class="small">Visualize no Google Maps</a>
                            </div>
                            ${reviewsHtml || ''}
                        </div>
                    `;

            infowindow.setContent(content);
            infowindow.open(map, marker);

            try {
                const svService = new google.maps.StreetViewService();
                const loc = marker.getPosition();
                svService.getPanorama({ location: loc, radius: 100 }, (result, status) => {
                    const el = document.getElementById(panoId);
                    if (!el) return;
                    if (status === google.maps.StreetViewStatus.OK && result && result.location) {
                        new google.maps.StreetViewPanorama(el, { pano: result.location.pano, pov: { heading: 270, pitch: 0 }, disableDefaultUI: true });
                    }
                });
            } catch (ex) { console.warn('StreetView load failed', ex); }
        }

        fetch('/Barbershops/GetReviews?placeId=' + encodeURIComponent(place.placeId || ''))
            .then(r => r.json())
            .then(data => {
                let reviewsHtml = '';
                if (data && data.success && data.reviews) {
                    try {
                        const j = typeof data.reviews === 'string' ? JSON.parse(data.reviews) : data.reviews;
                        if (j.result && j.result.reviews) {
                            reviewsHtml = '<div class="mt-2 small">';
                            j.result.reviews.slice(0,3).forEach(rv => { reviewsHtml += `<div class="mb-1"><strong>${rv.author_name}</strong>: ${rv.text.substring(0,120)}${rv.text.length>120?'...':''}</div>`; });
                            reviewsHtml += '</div>';
                        }
                    } catch (ex) { console.warn(ex); }
                }
                buildContent(null, reviewsHtml);

                if (place.placeId && typeof placesService !== 'undefined' && placesService) {
                    try {
                        placesService.getDetails({ placeId: place.placeId, fields: ['name','formatted_address','formatted_phone_number','opening_hours','website','photos','rating','url'] }, (details, status) => {
                            if (status === google.maps.places.PlacesServiceStatus.OK && details) {
                                const photos = details.photos && details.photos.length ? details.photos.map(p => p.getUrl({ maxWidth: 400 })) : [];
                                const detailsInfo = { formatted_phone_number: details.formatted_phone_number, opening_hours: details.opening_hours, website: details.website, photos: photos };
                                buildContent(detailsInfo, reviewsHtml);
                            }
                        });
                    } catch (ex) { console.warn('places.getDetails failed', ex); }
                }
            })
            .catch(err => { buildContent(null, ''); });
    };

    if ((!place.placeId || place.placeId === '') && typeof placesService !== 'undefined' && placesService) {
        try {
            const query = (place.name || '') + ' ' + (place.address || '');
            placesService.findPlaceFromQuery({ input: query, fields: ['place_id'] }, (res, status) => {
                if (status === google.maps.places.PlacesServiceStatus.OK && res && res.length) { place.placeId = res[0].place_id; }
                renderWithData();
            });
        } catch (ex) { console.warn('findPlaceFromQuery failed', ex); renderWithData(); }
    } else {
        renderWithData();
    }
    const address = place.address || '';
    const rating = (typeof place.rating === 'number' && place.rating > 0) ? `<span class="badge bg-warning text-dark">${place.rating.toFixed(1)} <i class="fas fa-star"></i></span>` : '';
    const detailsLink = '/Barbershops/Details/' + (place.id || '');
    const imageHtml = place.image ? `<div class="mb-2 text-center"><img src="${place.image}" alt="${place.name}" style="max-width:100%; max-height:120px; object-fit:cover;"/></div>` : '';

    const panoId = 'svPanorama_' + Math.random().toString(36).substr(2, 9);
    const mapsUrl = place.placeId ? ('https://www.google.com/maps/place/?q=place_id:' + encodeURIComponent(place.placeId)) : ('https://www.google.com/maps/search/?api=1&query=' + encodeURIComponent(address));

    function buildContent(detailsInfo, reviewsHtml) {
        const phoneHtml = detailsInfo && detailsInfo.formatted_phone_number ? `<div class="mb-1"><a href=\"tel:${detailsInfo.formatted_phone_number}\" class="small">${detailsInfo.formatted_phone_number}</a></div>` : '';
        const hoursHtml = detailsInfo && detailsInfo.opening_hours && detailsInfo.opening_hours.weekday_text ? `<div class="small text-muted mb-1">${detailsInfo.opening_hours.weekday_text.slice(0,3).join(' / ')}</div>` : '';
        const websiteHtml = detailsInfo && detailsInfo.website ? `<div class="mt-1"><a target=\"_blank\" href=\"${detailsInfo.website}\" class="small">Visitar site</a></div>` : '';
        const photoHtml = detailsInfo && detailsInfo.photos && detailsInfo.photos.length ? `<div class="mb-2 text-center"><img src=\"${detailsInfo.photos[0]}\" alt=\"${place.name}\" style=\"max-width:100%; max-height:120px; object-fit:cover;\"/></div>` : imageHtml;
        const content = `
            <div class="p-2 text-start">
                ${photoHtml}
                <h6 class="mb-1">${place.name}</h6>
                <p class="small text-muted mb-2">${address}</p>
                <div class="mb-2">${rating}</div>
                ${phoneHtml}
                ${hoursHtml}
                ${websiteHtml}
                <a href="${detailsLink}" class="btn btn-sm btn-primary mt-1">Ver detalhes</a>
                <div id="${panoId}" class="mt-2" style="width:100%; height:140px; background:#f5f5f5; display:flex; align-items:center; justify-content:center;">
                    <a target="_blank" href="${mapsUrl}" class="small">Visualize no Google Maps</a>
                </div>
                ${reviewsHtml || ''}
            </div>
        `;

        infowindow.setContent(content);
        infowindow.open(map, marker);
    }

    fetch('/Barbershops/GetReviews?placeId=' + encodeURIComponent(place.placeId || ''))
        .then(r => r.json())
        .then(data => {
            let reviewsHtml = '';
            if (data && data.success && data.reviews) {
                try {
                    const j = typeof data.reviews === 'string' ? JSON.parse(data.reviews) : data.reviews;
                    if (j.result && j.result.reviews) {
                        reviewsHtml = '<div class="mt-2 small">';
                        j.result.reviews.slice(0,3).forEach(rv => { reviewsHtml += `<div class="mb-1"><strong>${rv.author_name}</strong>: ${rv.text.substring(0,120)}${rv.text.length>120?'...':''}</div>`; });
                        reviewsHtml += '</div>';
                    }
                } catch (ex) { console.warn(ex); }
            }

            buildContent(null, reviewsHtml);

            if (place.placeId && typeof placesService !== 'undefined' && placesService) {
                try {
                    placesService.getDetails({ placeId: place.placeId, fields: ['name','formatted_address','formatted_phone_number','opening_hours','website','photos','rating','url'] }, (details, status) => {
                        if (status === google.maps.places.PlacesServiceStatus.OK && details) {
                            const photos = details.photos && details.photos.length ? details.photos.map(p => p.getUrl({ maxWidth: 400 })) : [];
                            const detailsInfo = { formatted_phone_number: details.formatted_phone_number, opening_hours: details.opening_hours, website: details.website, photos: photos };
                            buildContent(detailsInfo, reviewsHtml);

                            const sv = new google.maps.StreetViewService();
                            const loc = marker.getPosition();
                            sv.getPanorama({ location: loc, radius: 100 }, (result, svStatus) => {
                                const el = document.getElementById(panoId);
                                if (!el) return;
                                if (svStatus === google.maps.StreetViewStatus.OK && result && result.location) {
                                    new google.maps.StreetViewPanorama(el, { pano: result.location.pano, pov: { heading: 270, pitch: 0 }, disableDefaultUI: true });
                                }
                            });
                        }
                    });
                } catch (ex) { console.warn('places.getDetails failed', ex); }
            }
        })
        .catch(err => { buildContent(null, ''); });
}

function showGooglePOIInfo(placeId, latLng) {
    const loading = document.getElementById('mapLoading'); if (loading) loading.style.display = 'block';

    placesService.getDetails({ placeId: placeId, fields: ['name', 'formatted_address', 'rating', 'reviews', 'photos', 'formatted_phone_number'] }, (place, status) => {
        if (loading) loading.style.display = 'none';

        if (status === google.maps.places.PlacesServiceStatus.OK && place) {
            const address = place.formatted_address || '';

            let ratingHtml = '';
            if (typeof place.rating === 'number' && place.rating > 0) {
                ratingHtml = `<div class="rating mb-2">`;
                for (let i = 1; i <= 5; i++) { if (i <= Math.round(place.rating)) { ratingHtml += '<i class="fas fa-star"></i>'; } else { ratingHtml += '<i class="far fa-star"></i>'; } }
                ratingHtml += ` <span class="text-muted ms-1">(${place.rating.toFixed(1)})</span></div>`;
            }

            let imageHtml = '';
            if (place.photos && place.photos.length > 0) {
                const photoUrl = place.photos[0].getUrl({ maxWidth: 300, maxHeight: 150 });
                imageHtml = `<div class="mb-2 text-center"><img src="${photoUrl}" alt="${place.name}" style="max-width:100%; max-height:120px; object-fit:cover; border-radius: 8px;"/></div>`;
            }

            let reviewsHtml = '';
            if (place.reviews && place.reviews.length > 0) {
                reviewsHtml = '<div class="mt-2 pt-2 border-top small" style="max-height: 125px; overflow-y: auto;">';
                place.reviews.slice(0, 3).forEach(rv => { let stars = '★'.repeat(rv.rating) + '☆'.repeat(5 - rv.rating); reviewsHtml += `<div class="mb-2" style="border-bottom:1px solid #f0f0f0; padding-bottom:4px;">\n                                <div class="d-flex justify-content-between align-items-center mb-1">\n                                    <strong style="font-size:0.75rem;">${rv.author_name}</strong>\n                                    <span class="text-warning" style="font-size:0.7rem;">${stars}</span>\n                                </div>\n                                <div class="text-muted" style="font-size:0.7rem; line-height:1.25;">${rv.text.substring(0, 100)}${rv.text.length > 100 ? '...' : ''}</div>\n                            </div>`; });
                reviewsHtml += '</div>';
            } else {
                reviewsHtml = '<div class="mt-2 pt-2 border-top small text-muted text-center">Sem comentários disponíveis no Google Maps.</div>';
            }

            const phoneButton = place.formatted_phone_number ? `<a href="tel:${place.formatted_phone_number}" class="btn btn-sm btn-outline-secondary flex-fill"><i class="fas fa-phone"></i> Ligar</a>` : '';
            const actionButton = `<button class="btn btn-sm btn-primary flex-fill me-1" onclick="alert('Este estabelecimento não está registado na nossa plataforma. Utilize o contacto telefónico para agendar.')"><i class="fas fa-calendar-alt"></i> Agendar</button>`;

            const content = `
                <div class="p-2 text-start" style="width: 260px;">
                    ${imageHtml}
                    <h6 class="mb-1 fw-bold text-primary-custom">${place.name}</h6>
                    <p class="small text-muted mb-2" style="font-size: 0.75rem; line-height: 1.3;">${address}</p>
                    ${ratingHtml}
                    <div class="d-flex mb-2">
                        ${actionButton}
                        ${phoneButton}
                    </div>
                    <div class="small fw-bold mt-2 text-secondary"><i class="fab fa-google text-danger"></i> Comentários do Google Maps:</div>
                    ${reviewsHtml}
                </div>
            `;

            infowindow.setContent(content);
            infowindow.setPosition(latLng);
            infowindow.open(map);
            const panel = document.getElementById('placeDetails'); if (panel) panel.innerHTML = content.replace(/style="width: 260px;"/, ''); panel.style.display = 'block';
        }
    });
}

function renderPlaceDetailsPanel(place) {
    const panel = document.getElementById('placeDetails'); if (!panel) return;
    const html = `
        <div class="card shadow-sm">
            <div class="card-body">
                <h6 class="mb-1 fw-bold">${place.name}</h6>
                <p class="small text-muted mb-2">${place.address || ''}</p>
                <div class="mb-2">${(typeof place.rating === 'number' && place.rating>0) ? `<span class=\"badge bg-warning text-dark\">${place.rating.toFixed(1)} <i class=\"fas fa-star\"></i></span>` : ''}</div>
                <div class="d-grid gap-2">
                    <a href="/Barbershops/Details/${place.id}" class="btn btn-primary btn-sm">Ver Detalhes</a>
                    ${place.phone ? `<a href=\"tel:${place.phone}\" class=\"btn btn-outline-secondary btn-sm\">Ligar</a>` : ''}
                </div>
            </div>
        </div>
    `;
    panel.innerHTML = html;
    panel.style.display = 'block';
}

function updateSidebar() {
    const listContainer = document.getElementById('locationsList');
    listContainer.innerHTML = '';

    const bounds = map.getBounds();
    if (!bounds) { listContainer.innerHTML = '<div class="text-center text-muted small mt-4">Nenhum resultado visível nesta área</div>'; return; }

    let visibleCount = 0;
    serverMarkers.forEach(entry => {
        const marker = entry.marker;
        const data = entry.data;
        if (!marker || !marker.getPosition) return;
        if (bounds.contains(marker.getPosition())) {
            visibleCount++;
            if (visibleCount > 25) return;

            const placeName = data.name || marker.getTitle();
            const itemDiv = document.createElement('div');
            itemDiv.className = 'card mb-2 shadow-sm border-0';
            itemDiv.style.cursor = 'pointer';

            const ratingHtml = (typeof data.rating === 'number' && data.rating > 0) ? `<div class="text-warning small">${'★'.repeat(Math.round(data.rating))}</div>` : '';

            itemDiv.innerHTML = `
                <div class="card-body p-2">
                    <div class="d-flex justify-content-between align-items-start">
                        <h6 class="mb-0 small fw-bold">${placeName}</h6>
                        <span class="badge bg-secondary" style="font-size: 0.6rem;">Recomendado</span>
                    </div>
                    <p class="mb-0 text-muted" style="font-size: 0.75rem;">${data.address || ''}</p>
                    ${ratingHtml}
                    ${data.hasMobile ? `<div class="mt-2 small text-success">Atendimento em casa disponível - Est. ${data.distanceKm ? data.distanceKm + ' km' : ''} ${data.estimatedTravelFee ? '- Taxa: €' + data.estimatedTravelFee : ''}</div>` : ''}
                    <div class="mt-2">
                        <a href="/Bookings/Create?barbershopId=${data.id}${userMarker && userMarker.getPosition ? '&userLat=' + userMarker.getPosition().lat() + '&userLng=' + userMarker.getPosition().lng() : ''}" class="btn btn-sm btn-outline-primary me-2">Solicitar On-site</a>
                        ${data.phone ? `<a href=\"tel:${data.phone}\" class=\"btn btn-sm btn-outline-secondary\">Ligar</a>` : ''}
                    </div>
                </div>
            `;

            itemDiv.onclick = () => { map.panTo(marker.getPosition()); google.maps.event.trigger(marker, 'click'); };

            listContainer.appendChild(itemDiv);
        }
    });

    if (visibleCount === 0) { listContainer.innerHTML = '<div class="text-center text-muted small mt-4">Nenhum resultado visível nesta área</div>'; }
}

function setupUIListeners() {
    const checkAll = document.getElementById('checkAll');
    const filters = document.querySelectorAll('.category-filter');
    const mobileOnly = document.getElementById('mobileOnly');
    const genderSelect = document.getElementById('genderSelect');

    const locateBtn = document.getElementById('locateBtn');
    if (locateBtn) { locateBtn.addEventListener('click', () => { hasUserInteracted = false; locateUser(); }); }

    checkAll.addEventListener('change', function() { filters.forEach(cb => cb.checked = this.checked); searchPlacesInBounds(); });

    if (mobileOnly) mobileOnly.addEventListener('change', () => searchPlacesInBounds());
    if (genderSelect) genderSelect.addEventListener('change', () => searchPlacesInBounds());

    filters.forEach(cb => { cb.addEventListener('change', () => { checkAll.checked = Array.from(filters).every(f => f.checked); searchPlacesInBounds(); }); });

    window.getSelectedGenders = function() { const sel = document.getElementById('genderSelect'); return sel && sel.value ? sel.value : ''; }
}

function updateUserMarker(latLng, accuracy, heading) {
    if (typeof heading === 'number' && !isNaN(heading)) {
        const arrowSymbol = { path: 'M0,-10 L6,6 L0,2 L-6,6 Z', scale: 1.8, fillColor: '#4285F4', fillOpacity: 1, strokeColor: '#ffffff', strokeWeight: 1, rotation: heading };
        if (!userMarker) { userMarker = new google.maps.Marker({ position: latLng, map: map, title: 'Você está aqui', icon: arrowSymbol }); }
        else { userMarker.setPosition(latLng); userMarker.setIcon(arrowSymbol); userMarker.setMap(map); }
    } else {
        const circleSymbol = { path: google.maps.SymbolPath.CIRCLE, scale: 9, fillColor: '#1A73E8', fillOpacity: 1, strokeColor: '#ffffff', strokeWeight: 3 };
        if (!userMarker) { userMarker = new google.maps.Marker({ position: latLng, map: map, title: 'Você está aqui', icon: circleSymbol }); }
        else { userMarker.setPosition(latLng); userMarker.setIcon(circleSymbol); userMarker.setMap(map); }
    }

    if (!accuracyCircle) { accuracyCircle = new google.maps.Circle({ strokeColor: '#4285F4', strokeOpacity: 0.3, strokeWeight: 1, fillColor: '#4285F4', fillOpacity: 0.12, map: map, center: latLng, radius: accuracy || 0 }); }
    else { accuracyCircle.setCenter(latLng); accuracyCircle.setRadius(accuracy || 0); accuracyCircle.setMap(map); }
}

function locateUser() {
    if (!navigator.geolocation) { return; }
    const loading = document.getElementById('mapLoading'); if (loading) loading.style.display = 'block';
    if (geoWatchId !== null) { navigator.geolocation.clearWatch(geoWatchId); geoWatchId = null; }

    navigator.geolocation.getCurrentPosition((pos) => {
        const lat = pos.coords.latitude; const lng = pos.coords.longitude; const accuracy = pos.coords.accuracy || 0; const heading = (typeof pos.coords.heading === 'number') ? pos.coords.heading : null; const latLng = new google.maps.LatLng(lat, lng);
        updateUserMarker(latLng, accuracy, heading); map.panTo(latLng); map.setZoom(Math.max(map.getZoom(), 14)); searchPlacesInBounds(); loading.style.display = 'none';
        geoWatchId = navigator.geolocation.watchPosition((p) => { const l = p.coords.latitude; const ln = p.coords.longitude; const a = p.coords.accuracy || 0; const h = (typeof p.coords.heading === 'number') ? p.coords.heading : null; const ll = new google.maps.LatLng(l, ln); updateUserMarker(ll, a, h); }, (err) => { }, { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
    }, (err) => {
        loading.style.display = 'none';
        if (document.activeElement && document.activeElement.id === 'locateBtn') {
            switch (err.code) { case err.PERMISSION_DENIED: alert('Permissão de geolocalização negada.'); break; case err.POSITION_UNAVAILABLE: alert('Posição indisponível.'); break; case err.TIMEOUT: alert('Tempo de geolocalização esgotado.'); break; default: alert('Erro ao obter localização.'); }
        }
    }, { enableHighAccuracy: true, timeout: 10000, maximumAge: 0 });
}

window.onload = initMap;
