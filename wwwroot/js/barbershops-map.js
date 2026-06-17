// BarberLoc Interactive Map — barbershops-map.js
// Handles: map init, marker creation, filter panel, place detail offcanvas,
//          photo carousel, reviews, opening hours, and favourites toggle.
//
// ── BUG FIXES APPLIED ───────────────────────────────────────────────────────
//
// FIX 1 (Markers disappearing):
//   Root cause: Google Maps script was injected dynamically via createElement,
//   creating a race condition with this file loading. If initMap fired before
//   this script executed, the callback was undefined and the map never rendered.
//   Fix: initMap is declared synchronously here as window.initMap. The HTML
//   now loads this script first, then the Maps API via a static <script> tag
//   with async+defer, which guarantees initMap is on window before the API fires.
//   The #map container now has an explicit pixel height (not 100% / calc) so it
//   never collapses to zero height regardless of parent layout.
//
// FIX 2 (Markers not clickable):
//   Root cause: The Maps API was loaded with libraries=places,marker. The
//   presence of the `marker` library causes google.maps.Marker (legacy) to
//   behave differently — click listeners may silently fail to attach in newer
//   API versions when the marker library is also loaded.
//   Fix: Remove `libraries=marker` from the script URL. Use only the core Maps
//   JS without extra libraries (Places calls are all server-side via our proxy).
//   Markers are stored in module-scoped `markers` array immediately on creation.
//
// FIX 3 (External Google Maps link instead of in-site panel):
//   Root cause: handleMarkerClick had an early return guard: `if (!offcanvasInstance) return`.
//   Bootstrap was initialized inside initMap, but if offcanvasInstance was null
//   (e.g. Bootstrap not yet ready, or panel element missing), the entire click
//   handler bailed out silently. The native Maps click behavior then took over,
//   opening the default Google Maps popup with an external link.
//   Fix: Offcanvas is initialized in a DOMContentLoaded listener separate from
//   initMap. The guard is removed and replaced with a lazy-init fallback.
//   clickableIcons: false suppresses all POI default popups on the map.
//   google.maps.event.addListener(map, 'click', e => {}) absorbs stray map clicks.
// ─────────────────────────────────────────────────────────────────────────────

'use strict';

// ── Module-scope state (stored at module level to prevent GC of markers) ──────
let map;
let markers = [];          // Kept in module scope — preventing GC is critical for marker persistence
let allPlaces = [];
let activeFilters = { category: '', minRating: '', mobileOnly: false };
let offcanvasInstance = null;
let currentPlaceId = null;

// ── Offcanvas: initialise as soon as the DOM is ready ────────────────────────
// Separate from initMap so it's always ready before any marker click fires.
document.addEventListener('DOMContentLoaded', () => {
    const panelEl = document.getElementById('placeDetailPanel');
    if (panelEl && typeof bootstrap !== 'undefined') {
        offcanvasInstance = new bootstrap.Offcanvas(panelEl, { backdrop: true, scroll: false });
    }
});

// ── Map Initialisation (called by Google Maps API callback=initMap) ───────────
// This function MUST be on window before the Maps API script fires its callback.
// The HTML loads this file first (non-async, non-defer), then the Maps script
// with async+defer, guaranteeing the ordering.
window.initMap = function () {
    const defaultCenter = {
        lat: typeof window.BarberLocConfig?.defaultLat === 'number'
            ? window.BarberLocConfig.defaultLat : 38.7169,
        lng: typeof window.BarberLocConfig?.defaultLng === 'number'
            ? window.BarberLocConfig.defaultLng : -9.1399
    };

    map = new google.maps.Map(document.getElementById('map'), {
        zoom: 13,
        center: defaultCenter,
        mapTypeControl: false,
        fullscreenControl: true,
        streetViewControl: false,
        // FIX 3: Disable all clickable POI icons — prevents native Google Maps
        // info windows from opening when the user clicks anywhere on the map.
        clickableIcons: false,
        mapId: 'DEMO_MAP_ID', // Required for AdvancedMarkerElement to render!
        styles: [
            { featureType: 'poi', stylers: [{ visibility: 'off' }] },
            { featureType: 'transit', stylers: [{ visibility: 'simplified' }] }
        ]
    });

    // FIX 3: Absorb any stray map-level click events so Google's default popup
    // handler never fires — our marker listeners take priority.
    google.maps.event.addListener(map, 'click', () => { /* intentionally empty */ });

    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(function(position) {
            const userPos = {
                lat: position.coords.latitude,
                lng: position.coords.longitude
            };

            // Create the pulsing blue dot element
            const pinElement = document.createElement('div');
            pinElement.className = 'user-location-marker';
            pinElement.innerHTML = '<div class="blue-dot"></div><div class="blue-dot-pulse"></div>';

            // Use the modern AdvancedMarkerElement required by Google Maps in 2026
            const userMarker = new google.maps.marker.AdvancedMarkerElement({
                map: map,
                position: userPos,
                content: pinElement,
                title: "A sua localização"
            });
        }, function() {
            // Handle geolocation denial gracefully - do nothing so map doesn't crash
        });
    }

    // If offcanvas wasn't ready at DOMContentLoaded (rare), lazy-init it now.
    if (!offcanvasInstance) {
        const panelEl = document.getElementById('placeDetailPanel');
        if (panelEl && typeof bootstrap !== 'undefined') {
            offcanvasInstance = new bootstrap.Offcanvas(panelEl, { backdrop: true, scroll: false });
        }
    }

    setupFilterPanel();
    loadMarkers();
};

// ── Load all barbershop markers from server ───────────────────────────────────
async function loadMarkers() {
    showMapLoading(true);

    try {
        const resp = await fetch('/Barbershops/GetMapData');
        if (!resp.ok) throw new Error(`HTTP ${resp.status}`);
        allPlaces = await resp.json();
        renderMarkers(allPlaces);
        updateResultsCount(allPlaces.length);
    } catch (err) {
        console.error('[BarberLoc] Failed to load barbershops:', err);
        updateResultsCount(0);
    } finally {
        showMapLoading(false);
    }
}

// ── Render / re-render markers from a place list ──────────────────────────────
// FIX 1 + 2: Markers are assigned to `map` immediately on construction and
// stored in the module-scoped `markers` array. This prevents garbage collection
// and ensures the reference stays alive for the lifetime of the page.
function renderMarkers(places) {
    // Clear existing markers by detaching from the map
    markers.forEach(m => m.setMap(null));
    markers = [];

    places.forEach(place => {
        // FIX 2: Using google.maps.Marker (legacy, stable) without the `marker` library.
        // addListener('click') is fully supported and reliable on this constructor.
        const marker = new google.maps.Marker({
            position: { lat: place.lat, lng: place.lng },
            map: map,                      // attached immediately — not deferred
            title: place.name,
            label: {
                text: place.rating ? place.rating.toFixed(1) : '?',
                color: '#fff',
                fontWeight: 'bold',
                fontSize: '11px'
            },
            icon: buildMarkerIcon(place.category),
            // Ensure the marker is above all other map elements
            zIndex: google.maps.Marker.MAX_ZINDEX + 1
        });

        // Attach place metadata directly to the marker object
        marker._placeId      = place.placeId;
        marker._placeName    = place.name;
        marker._placeAddress = place.address;
        marker._placeData    = place;

        // FIX 2: listener attached immediately after marker creation.
        // google.maps.event.addListener is used (not the shorthand) to be explicit
        // and to allow future removal if needed.
        google.maps.event.addListener(marker, 'click', function () {
            handleMarkerClick(this);
        });

        // Store in module-scope array — prevents GC from destroying the marker object
        markers.push(marker);
    });
}

// ── Marker icon builder by category ──────────────────────────────────────────
function buildMarkerIcon(category) {
    const colours = {
        Barbershop: '#3b5bdb',   // indigo
        HairSalon:  '#d6336c',   // pink
        Unisex:     '#0ca678',   // teal
        default:    '#495057'
    };
    const fill = colours[category] || colours.default;

    return {
        path: google.maps.SymbolPath.CIRCLE,
        fillColor: fill,
        fillOpacity: 1,
        strokeColor: '#fff',
        strokeWeight: 2,
        scale: 14
    };
}

// ── Marker click: open offcanvas + fetch full details ─────────────────────────
// FIX 3: No early return on missing offcanvasInstance.
// If it's still null (extremely unlikely given DOMContentLoaded ordering), we
// attempt a lazy-init before proceeding. The offcanvas will always open.
async function handleMarkerClick(marker) {
    // Lazy-init guard (belt-and-suspenders for the DOMContentLoaded race)
    if (!offcanvasInstance) {
        const panelEl = document.getElementById('placeDetailPanel');
        if (panelEl && typeof bootstrap !== 'undefined') {
            offcanvasInstance = new bootstrap.Offcanvas(panelEl, { backdrop: true, scroll: false });
        }
        if (!offcanvasInstance) {
            console.warn('[BarberLoc] Bootstrap Offcanvas not available.');
            return;
        }
    }

    currentPlaceId = marker._placeId;

    // Show loading state and open the panel immediately for perceived performance
    setPanelState('loading');
    offcanvasInstance.show();

    // Pre-fill the header title with the locally-known name while fetch is in flight
    setTextField('name', marker._placeName || 'Barbearia');

    // Determine the best endpoint — prefer /Map/Details if available,
    // fall back to the existing /Barbershops/PlaceDetails endpoint.
    const detailsEndpoint = currentPlaceId
        ? `/Barbershops/PlaceDetails?placeId=${encodeURIComponent(currentPlaceId)}`
        : null;

    if (!detailsEndpoint) {
        // No PlaceId stored — render with whatever data the marker has
        fillBasicPanelFromMarker(marker._placeData);
        setPanelState('content');
        return;
    }

    try {
        const resp = await fetch(detailsEndpoint);

        if (!resp.ok) {
            console.error(`[BarberLoc] PlaceDetails returned HTTP ${resp.status}`);
            setPanelState('error');
            return;
        }

        const data = await resp.json();

        if (!data.success) {
            console.error('[BarberLoc] PlaceDetails success=false:', data.message);
            setPanelState('error');
            return;
        }

        populatePanel(data);
        setPanelState('content');
    } catch (err) {
        console.error('[BarberLoc] PlaceDetails fetch error:', err);
        setPanelState('error');
    }
}

// ── Populate panel with full place details ────────────────────────────────────
function populatePanel(d) {
    // Name
    setTextField('name', d.name || '—');

    // Demo Mode / Mock data badge
    const isDemo = d.isDemoMode || d.isMock || false;
    el('mock-badge')?.classList.toggle('d-none', !isDemo);

    // Reviews header — toggle between Google and local-seed icons
    const googleIcon     = el('reviews-google-icon');
    const demoIcon       = el('reviews-demo-icon');
    const localBadge     = el('local-reviews-badge');
    const reviewsHeading = el('reviews-heading-text');
    if (googleIcon && demoIcon && localBadge && reviewsHeading) {
        googleIcon.classList.toggle('d-none', isDemo);
        demoIcon.classList.toggle('d-none', !isDemo);
        localBadge.classList.toggle('d-none', !isDemo);
        reviewsHeading.textContent = isDemo ? 'Avaliações Locais' : 'Avaliações Google';
    }

    // Star rating
    renderStars('panel-stars', d.rating);
    setTextField('rating', d.rating ? d.rating.toFixed(1) : '—');
    setTextField('ratings-total',
        d.userRatingsTotal
            ? `(${d.userRatingsTotal.toLocaleString('pt-PT')} avaliações)`
            : '');

    // Open/Closed badge
    const openBadge = el('open-badge');
    if (d.isOpenNow !== null && d.isOpenNow !== undefined) {
        openBadge.classList.remove('d-none', 'bg-success', 'bg-danger');
        openBadge.classList.add(d.isOpenNow ? 'bg-success' : 'bg-danger');
        openBadge.textContent = d.isOpenNow ? 'Aberto agora' : 'Fechado';
    } else {
        openBadge.classList.add('d-none');
    }

    // Address
    setTextField('address', d.formattedAddress || '—');

    // Phone — rendered as a tel: link
    const phoneRow  = el('phone-row');
    const phoneLink = el('panel-phone');
    if (d.formattedPhoneNumber) {
        phoneLink.href = `tel:${d.formattedPhoneNumber}`;
        phoneLink.textContent = d.formattedPhoneNumber;
        phoneRow.classList.remove('d-none');
    } else {
        phoneRow.classList.add('d-none');
    }

    // Website — external link with rel="noopener noreferrer"
    const webRow  = el('website-row');
    const webLink = el('panel-website');
    if (d.website) {
        webLink.href = d.website;
        webLink.textContent = d.website.replace(/^https?:\/\//, '').replace(/\/$/, '');
        webRow.classList.remove('d-none');
    } else {
        webRow.classList.add('d-none');
    }

    // Opening hours
    const hoursSection = el('hours-section');
    const hoursList    = el('panel-hours-list');
    if (d.weekdayText && d.weekdayText.length > 0) {
        hoursList.innerHTML = d.weekdayText.map(day => {
            const colonIdx = day.indexOf(':');
            const dayName  = colonIdx > -1 ? day.substring(0, colonIdx) : day;
            const hours    = colonIdx > -1 ? day.substring(colonIdx + 1).trim() : '';
            const isClosed = hours.toLowerCase().includes('fechado') || hours.toLowerCase().includes('closed');
            return `<li class="d-flex justify-content-between py-1 border-bottom border-light">
                        <span class="fw-medium">${escHtml(dayName)}</span>
                        <span class="text-muted${isClosed ? ' text-danger' : ''}">${escHtml(hours)}</span>
                    </li>`;
        }).join('');
        hoursSection.classList.remove('d-none');
    } else {
        hoursSection.classList.add('d-none');
    }

    // Photos carousel
    renderPhotoCarousel(d.photos || []);

    // Reviews
    renderReviews(d.reviews || []);

    // Directions button — show it and store destination on data attribute
    const directionsBtn = el('btn-directions');
    if (directionsBtn) {
        directionsBtn.classList.remove('d-none');
        directionsBtn.dataset.destination = d.formattedAddress || d.name || '';
        directionsBtn.dataset.lat = (d.lat ?? d.latitude ?? '') + '';
        directionsBtn.dataset.lng = (d.lng ?? d.longitude ?? '') + '';
    }
    // Collapse any previously open directions panel
    el('directions-embed-container')?.classList.add('d-none');
    const existingIframe = el('directions-iframe');
    if (existingIframe) {
        existingIframe.style.display = 'none';
        existingIframe.src = '';
    }

    // Favourite button
    setupFavouriteButton(d);
}

// ── Fill panel from basic marker data (no Place ID) ──────────────────────────
function fillBasicPanelFromMarker(place) {
    setTextField('name',    place.name    || '—');
    setTextField('address', place.address || '—');
    setTextField('rating',  place.rating  ? place.rating.toFixed(1) : '—');
    renderStars('panel-stars', place.rating);
    setTextField('ratings-total', '');

    el('open-badge')?.classList.add('d-none');
    el('phone-row')?.classList.add('d-none');
    el('website-row')?.classList.add('d-none');
    el('hours-section')?.classList.add('d-none');
    el('reviews-section')?.classList.add('d-none');
    el('photo-carousel-wrapper')?.classList.add('d-none');
    el('photo-placeholder')?.classList.remove('d-none');
    el('mock-badge')?.classList.add('d-none');
    el('btn-favourite')?.classList.add('d-none');

    // Reset review header to default (Google) state
    el('reviews-google-icon')?.classList.remove('d-none');
    el('reviews-demo-icon')?.classList.add('d-none');
    el('local-reviews-badge')?.classList.add('d-none');
    const rh = el('reviews-heading-text');
    if (rh) rh.textContent = 'Avaliações Google';

    const directionsBtn2 = el('btn-directions');
    if (directionsBtn2) {
        directionsBtn2.classList.remove('d-none');
        directionsBtn2.dataset.destination = place.address || place.name || '';
        directionsBtn2.dataset.lat = (place.lat ?? '') + '';
        directionsBtn2.dataset.lng = (place.lng ?? '') + '';
    }
    el('directions-embed-container')?.classList.add('d-none');
    const existingIframe2 = el('directions-iframe');
    if (existingIframe2) {
        existingIframe2.style.display = 'none';
        existingIframe2.src = '';
    }
}

// ── Photo Carousel ────────────────────────────────────────────────────────────
function renderPhotoCarousel(photos) {
    const wrapper    = el('photo-carousel-wrapper');
    const placeholder = el('photo-placeholder');
    const inner      = el('carousel-inner');
    const indicators = el('carousel-indicators');

    inner.innerHTML      = '';
    indicators.innerHTML = '';

    if (!photos || photos.length === 0) {
        wrapper.classList.add('d-none');
        placeholder.classList.remove('d-none');
        return;
    }

    placeholder.classList.add('d-none');
    wrapper.classList.remove('d-none');

    photos.forEach((photo, i) => {
        const active = i === 0 ? 'active' : '';
        inner.innerHTML += `
            <div class="carousel-item ${active}">
                <img src="${escHtml(photo.proxyUrl)}"
                     class="d-block w-100"
                     alt="Foto ${i + 1}"
                     loading="lazy"
                     onerror="this.closest('.carousel-item').remove()">
            </div>`;

        indicators.innerHTML += `
            <button type="button"
                    data-bs-target="#placePhotoCarousel"
                    data-bs-slide-to="${i}"
                    class="${active}"
                    ${active ? 'aria-current="true"' : ''}
                    aria-label="Foto ${i + 1}">
            </button>`;
    });
}

// ── Reviews ───────────────────────────────────────────────────────────────────
function renderReviews(reviews) {
    const section = el('reviews-section');
    const list    = el('panel-reviews-list');

    if (!reviews || reviews.length === 0) {
        section.classList.remove('d-none');
        list.innerHTML = `
            <div class="text-center py-4">
                <i class="fas fa-comment-slash fa-2x text-muted mb-2"></i>
                <p class="text-muted small mb-0">Ainda sem avaliações disponíveis</p>
            </div>
        `;
        return;
    }

    section.classList.remove('d-none');
    list.innerHTML = reviews.map((rv, idx) => {
        const stars  = buildStarHtml(rv.rating);
        const avatar = rv.profilePhotoUrl
            ? `<img src="${escHtml(rv.profilePhotoUrl)}" class="review-avatar me-2" alt="${escHtml(rv.authorName)}">`
            : `<div class="review-avatar me-2 d-flex align-items-center justify-content-center bg-secondary text-white fw-bold" style="font-size:14px;">${escHtml(rv.authorName.charAt(0))}</div>`;

        return `
            <div class="review-card">
                <div class="d-flex align-items-center mb-2">
                    ${avatar}
                    <div>
                        <div class="fw-semibold small">${escHtml(rv.authorName)}</div>
                        <div class="text-warning" style="font-size:11px;">${stars}</div>
                    </div>
                    <span class="ms-auto text-muted" style="font-size:11px;">${escHtml(rv.relativeTimeDescription || '')}</span>
                </div>
                ${rv.text ? `
                <div>
                    <p class="review-text mb-1 small text-muted" id="review-text-${idx}">${escHtml(rv.text)}</p>
                    <button class="btn btn-link btn-sm p-0 text-primary text-decoration-none"
                            style="font-size:11px;"
                            onclick="toggleReviewText(${idx})">Ler mais</button>
                </div>` : ''}
            </div>`;
    }).join('');
}

function toggleReviewText(idx) {
    const textEl = document.getElementById(`review-text-${idx}`);
    const btn = textEl?.nextElementSibling;
    if (!textEl) return;
    const expanded = textEl.classList.toggle('expanded');
    if (btn) btn.textContent = expanded ? 'Ler menos' : 'Ler mais';
}

// ── Stars ─────────────────────────────────────────────────────────────────────
function renderStars(containerId, rating) {
    el(containerId).innerHTML = buildStarHtml(rating);
}

function buildStarHtml(rating) {
    if (!rating) return '';
    let html = '';
    for (let i = 1; i <= 5; i++) {
        if (i <= Math.floor(rating)) {
            html += '<i class="fas fa-star"></i>';
        } else if (i === Math.ceil(rating) && rating % 1 >= 0.5) {
            html += '<i class="fas fa-star-half-alt"></i>';
        } else {
            html += '<i class="far fa-star"></i>';
        }
    }
    return html;
}

// ── Favourite button ──────────────────────────────────────────────────────────
// FIX: Previous implementation had a bug where it cloned the button, replaced
// it in the DOM incorrectly (replaceChild(newBtn, newBtn) — same node, no-op),
// and then added an event listener to the cloned node that was never inserted.
// Fix: Use data attribute as a state flag and replace listeners via cloneNode correctly.
function setupFavouriteButton(d) {
    const btn = el('btn-favourite');
    if (!btn) return;

    if (!window.BarberLocConfig?.isAuthenticated) {
        btn.classList.add('d-none');
        return;
    }

    btn.classList.remove('d-none');

    // Clone to remove any previously attached listeners (avoids double-firing)
    const newBtn = btn.cloneNode(true);
    btn.parentNode.replaceChild(newBtn, btn);   // FIX: replace btn with newBtn (not newBtn with itself)

    // Resolve the place ID from the data object first (works in both Google Maps and Leaflet contexts),
    // then fall back to the module-scope currentPlaceId (set by handleMarkerClick on the Google Maps page).
    const resolvedPlaceId = d.placeId || d.googlePlaceId || currentPlaceId;

    const updateFavBtn = (isFav) => {
        newBtn.dataset.fav = isFav ? 'true' : 'false';
        const ic = newBtn.querySelector('i');
        if (ic) ic.className = isFav ? 'fas fa-heart' : 'far fa-heart';
        newBtn.title = isFav ? 'Remover dos Favoritos' : 'Guardar nos Favoritos';
        newBtn.classList.toggle('btn-danger', isFav);
        newBtn.classList.toggle('btn-outline-danger', !isFav);
    };

    updateFavBtn(d.isFavourited ?? false);

    newBtn.addEventListener('click', async () => {
        const isFav    = newBtn.dataset.fav === 'true';
        const endpoint = isFav ? '/Barbershops/RemoveFavourite' : '/Barbershops/SaveFavourite';
        const token    = window.BarberLocConfig?.antiForgeryToken ?? '';

        try {
            const resp = await fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': token
                },
                body: JSON.stringify({
                    placeId:      resolvedPlaceId,
                    placeName:    d.name,
                    placeAddress: d.formattedAddress
                })
            });

            if (resp.ok) {
                const result = await resp.json();
                updateFavBtn(result.isFavourited);
            }
        } catch (err) {
            console.error('[BarberLoc] Favourite toggle error:', err);
        }
    });
}

// ── Panel state machine ───────────────────────────────────────────────────────
function setPanelState(state) {
    el('panel-loading').classList.toggle('d-none', state !== 'loading');
    el('panel-error').classList.toggle('d-none',   state !== 'error');
    el('panel-content').classList.toggle('d-none', state !== 'content');
}

// ── Filter panel setup ────────────────────────────────────────────────────────
function setupFilterPanel() {
    // Category / rating filter buttons
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const filter = btn.dataset.filter;
            const value  = btn.dataset.value;

            document.querySelectorAll(`.filter-btn[data-filter="${filter}"]`)
                .forEach(b => b.classList.remove('active'));
            btn.classList.add('active');

            activeFilters[filter === 'minRating' ? 'minRating' : 'category'] = value;
            applyFilters();
        });
    });

    // Mobile-only checkbox
    const mobileCb = el('filter-mobile');
    if (mobileCb) {
        mobileCb.addEventListener('change', () => {
            activeFilters.mobileOnly = mobileCb.checked;
            applyFilters();
        });
    }

    // Reset button
    const resetBtn = el('btn-reset-filters');
    if (resetBtn) {
        resetBtn.addEventListener('click', () => {
            activeFilters = { category: '', minRating: '', mobileOnly: false };
            document.querySelectorAll('.filter-btn').forEach(b => {
                b.classList.toggle('active', b.dataset.value === '');
            });
            if (mobileCb) mobileCb.checked = false;
            applyFilters();
        });
    }

    // Sidebar toggle
    const toggleBtn = el('toggle-sidebar-btn');
    const sidebar   = el('filter-sidebar');
    if (toggleBtn && sidebar) {
        toggleBtn.addEventListener('click', () => {
            const isOpen = !sidebar.classList.contains('collapsed-sidebar');
            sidebar.classList.toggle('collapsed-sidebar', isOpen);
            toggleBtn.classList.toggle('sidebar-open', !isOpen);
        });
    }
}

// ── Apply client-side filters ─────────────────────────────────────────────────
function applyFilters() {
    const filtered = allPlaces.filter(p => {
        if (activeFilters.minRating && p.rating < parseFloat(activeFilters.minRating)) return false;
        if (activeFilters.mobileOnly && !p.hasMobile) return false;

        if (activeFilters.category) {
            const lowerName = (p.name || '').toLowerCase();
            const category = p.category || '';
            
            if (activeFilters.category === 'Barbershop') {
                const isBarber = category === 'Barbershop' || 
                               lowerName.includes('barbearia') || 
                               lowerName.includes('barber') || 
                               lowerName.includes('dom') || 
                               lowerName.includes('men');
                if (!isBarber) return false;
            } else if (activeFilters.category === 'HairSalon') {
                const isSalon = category === 'HairSalon' || 
                              lowerName.includes('cabeleireiro') || 
                              lowerName.includes('salon') || 
                              lowerName.includes('studio') || 
                              lowerName.includes('beauty') || 
                              lowerName.includes('feminino');
                if (!isSalon) return false;
            } else if (activeFilters.category === 'Unisex') {
                if (category !== 'Unisex') return false;
            }
        }
        return true;
    });

    renderMarkers(filtered);
    updateResultsCount(filtered.length);
}

// ── Utilities ─────────────────────────────────────────────────────────────────
function el(id) { return document.getElementById(id); }

function setTextField(fieldName, value) {
    document.querySelectorAll(`[data-field="${fieldName}"]`).forEach(node => {
        node.textContent = value;
    });
}

function showMapLoading(show) {
    const overlay = el('map-loading');
    if (overlay) overlay.classList.toggle('d-none', !show);
}

function updateResultsCount(count) {
    const countEl = el('results-count');
    if (countEl) countEl.textContent = `${count} resultado${count !== 1 ? 's' : ''}`;
}

function escHtml(str) {
    if (!str) return '';
    return String(str)
        .replace(/&/g,  '&amp;')
        .replace(/</g,  '&lt;')
        .replace(/>/g,  '&gt;')
        .replace(/"/g,  '&quot;')
        .replace(/'/g,  '&#039;');
}

// ── Embedded Directions Toggle ────────────────────────────────────────────────
// Called by the "Como Chegar" button in _PlaceDetailPanel.
// Uses the browser Geolocation API for origin, then builds a Google Maps Embed
// Directions URL — the user never leaves BarberLoc.
window.toggleDirectionsPanel = function () {
    const container = el('directions-embed-container');
    const iframe    = el('directions-iframe');
    const loadingEl = el('directions-loading');
    const geoError  = el('directions-geo-error');
    const btn       = el('btn-directions');
    if (!container || !iframe || !btn) return;

    const isOpen = !container.classList.contains('d-none');
    if (isOpen) {
        // Collapse
        container.classList.add('d-none');
        iframe.style.display = 'none';
        iframe.src = '';
        return;
    }

    // Expand and load
    container.classList.remove('d-none');
    if (loadingEl) loadingEl.classList.remove('d-none');
    if (geoError)  geoError.classList.add('d-none');
    iframe.style.display = 'none';

    const destination = btn.dataset.destination || '';
    // Read the API key injected by the server (hidden input rendered by the layout/panel)
    const apiKey = document.getElementById('google-maps-api-key-panel')?.value || document.getElementById('google-maps-api-key')?.value || '';

    function buildEmbedUrl(originParam) {
        const base = 'https://www.google.com/maps/embed/v1/';
        let url = '';
        if (originParam) {
            url = `${base}directions?destination=${encodeURIComponent(destination)}&mode=walking&origin=${encodeURIComponent(originParam)}`;
        } else {
            url = `${base}place?q=${encodeURIComponent(destination)}`;
        }
        if (apiKey) url += `&key=${encodeURIComponent(apiKey)}`;
        return url;
    }

    function showIframe(src) {
        iframe.src = src;
        iframe.style.display = 'block';
        if (loadingEl) loadingEl.classList.add('d-none');
    }

    if (navigator.geolocation) {
        navigator.geolocation.getCurrentPosition(
            function (pos) {
                const origin = pos.coords.latitude + ',' + pos.coords.longitude;
                showIframe(buildEmbedUrl(origin));
            },
            function () {
                // Geolocation denied or unavailable — show map centred on destination
                if (geoError) geoError.classList.remove('d-none');
                showIframe(buildEmbedUrl(null));
            },
            { timeout: 6000, maximumAge: 60000 }
        );
    } else {
        if (geoError) geoError.classList.remove('d-none');
        showIframe(buildEmbedUrl(null));
    }
};