// BarberLoc Interactive Map — barbershops-map.js
// Handles: map init, marker creation, filter panel, place detail offcanvas,
//          photo carousel, reviews, opening hours, and favourites toggle.

'use strict';

// ── State ─────────────────────────────────────────────────────────────────────
let map;
let markers = [];
let allPlaces = [];
let activeFilters = { category: '', minRating: '', mobileOnly: false };
let offcanvasInstance = null;
let currentPlaceId = null;

// ── Map Initialisation (called by Google Maps API callback) ───────────────────
window.initMap = function () {
    const lisbon = { lat: 38.7169, lng: -9.1399 };

    map = new google.maps.Map(document.getElementById('map'), {
        zoom: 12,
        center: lisbon,
        mapTypeControl: false,
        fullscreenControl: true,
        streetViewControl: false,
        clickableIcons: false,
        styles: [
            { featureType: 'poi', stylers: [{ visibility: 'off' }] },
            { featureType: 'transit', stylers: [{ visibility: 'simplified' }] }
        ]
    });

    // Initialise Bootstrap offcanvas
    const panelEl = document.getElementById('placeDetailPanel');
    if (panelEl && typeof bootstrap !== 'undefined') {
        offcanvasInstance = new bootstrap.Offcanvas(panelEl, { backdrop: true, scroll: false });
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
    } finally {
        showMapLoading(false);
    }
}

// ── Render / re-render markers from a place list ──────────────────────────────
function renderMarkers(places) {
    // Clear existing markers
    markers.forEach(m => m.setMap(null));
    markers = [];

    places.forEach(place => {
        const marker = new google.maps.Marker({
            position: { lat: place.lat, lng: place.lng },
            map: map,
            title: place.name,
            label: {
                text: place.rating ? place.rating.toFixed(1) : '?',
                color: '#fff',
                fontWeight: 'bold',
                fontSize: '11px'
            },
            icon: buildMarkerIcon(place.category)
        });

        // Store place data on marker for click handler
        marker._placeId = place.placeId;
        marker._placeName = place.name;
        marker._placeAddress = place.address;
        marker._placeData = place;

        marker.addListener('click', () => handleMarkerClick(marker));
        markers.push(marker);
    });
}

// ── Marker icon builder by category ──────────────────────────────────────────
function buildMarkerIcon(category) {
    const colours = {
        Barbershop: '#3b5bdb',  // indigo
        HairSalon:  '#d6336c',  // pink
        Unisex:     '#0ca678',  // teal
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
async function handleMarkerClick(marker) {
    if (!offcanvasInstance) return;

    currentPlaceId = marker._placeId;

    // Reset panel state
    setPanelState('loading');
    offcanvasInstance.show();

    // Update header title immediately with the known name
    setTextField('name', marker._placeName || 'Barbearia');

    if (!currentPlaceId) {
        // No Place ID stored — show a basic panel with available data only
        fillBasicPanelFromMarker(marker._placeData);
        setPanelState('content');
        return;
    }

    try {
        const resp = await fetch(`/Barbershops/PlaceDetails?placeId=${encodeURIComponent(currentPlaceId)}`);

        if (!resp.ok) {
            setPanelState('error');
            return;
        }

        const data = await resp.json();

        if (!data.success) {
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
    // Name (already set in handleMarkerClick — update with API name if richer)
    setTextField('name', d.name || '—');

    // Mock badge
    el('mock-badge').classList.toggle('d-none', !d.isMock);

    // Star rating
    renderStars('panel-stars', d.rating);
    setTextField('rating', d.rating ? d.rating.toFixed(1) : '—');
    setTextField('ratings-total', d.userRatingsTotal ? `(${d.userRatingsTotal.toLocaleString('pt-PT')} avaliações)` : '');

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

    // Phone
    const phoneRow = el('phone-row');
    const phoneLink = el('panel-phone');
    if (d.formattedPhoneNumber) {
        phoneLink.href = `tel:${d.formattedPhoneNumber}`;
        phoneLink.textContent = d.formattedPhoneNumber;
        phoneRow.classList.remove('d-none');
    } else {
        phoneRow.classList.add('d-none');
    }

    // Website
    const webRow = el('website-row');
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
    const hoursList = el('panel-hours-list');
    if (d.weekdayText && d.weekdayText.length > 0) {
        hoursList.innerHTML = d.weekdayText.map(day => {
            const parts = day.split(':');
            const dayName = parts[0];
            const hours = parts.slice(1).join(':').trim();
            const isClosed = hours.toLowerCase().includes('fechado') || hours.toLowerCase().includes('closed');
            return `<li class="d-flex justify-content-between py-1 border-bottom border-light">
                        <span class="fw-medium">${escHtml(dayName)}</span>
                        <span class="text-muted ${isClosed ? 'text-danger' : ''}">${escHtml(hours)}</span>
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

    // Google Maps CTA
    const mapsBtn = el('btn-google-maps');
    if (d.googleMapsUrl) {
        mapsBtn.href = d.googleMapsUrl;
    } else {
        mapsBtn.href = `https://maps.google.com/?q=${encodeURIComponent(d.name || currentPlaceId)}`;
    }

    // Favourite button
    setupFavouriteButton(d);
}

// ── Fill panel from basic marker data (no Place ID) ──────────────────────────
function fillBasicPanelFromMarker(place) {
    setTextField('name', place.name || '—');
    setTextField('address', place.address || '—');
    setTextField('rating', place.rating ? place.rating.toFixed(1) : '—');
    renderStars('panel-stars', place.rating);
    setTextField('ratings-total', '');

    el('open-badge').classList.add('d-none');
    el('phone-row').classList.add('d-none');
    el('website-row').classList.add('d-none');
    el('hours-section').classList.add('d-none');
    el('reviews-section').classList.add('d-none');
    el('photo-carousel-wrapper').classList.add('d-none');
    el('photo-placeholder').classList.remove('d-none');
    el('mock-badge').classList.add('d-none');
    el('btn-favourite').classList.add('d-none');

    const mapsBtn = el('btn-google-maps');
    mapsBtn.href = `https://maps.google.com/?q=${encodeURIComponent(place.name || '')}`;
}

// ── Photo Carousel ────────────────────────────────────────────────────────────
function renderPhotoCarousel(photos) {
    const wrapper = el('photo-carousel-wrapper');
    const placeholder = el('photo-placeholder');
    const inner = el('carousel-inner');
    const indicators = el('carousel-indicators');

    inner.innerHTML = '';
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
    const list = el('panel-reviews-list');

    if (!reviews || reviews.length === 0) {
        section.classList.add('d-none');
        return;
    }

    section.classList.remove('d-none');
    list.innerHTML = reviews.map((rv, idx) => {
        const stars = buildStarHtml(rv.rating);
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
function setupFavouriteButton(d) {
    const btn = el('btn-favourite');
    const icon = el('fav-icon');

    if (!window.BarberLocConfig?.isAuthenticated) {
        btn.classList.add('d-none');
        return;
    }

    btn.classList.remove('d-none');
    updateFavBtn(d.isFavourited);

    // Remove old listener by replacing with clone
    const newBtn = btn.cloneNode(true);
    btn.parentNode.replaceChild(newBtn, newBtn);

    newBtn.addEventListener('click', async () => {
        const isFav = newBtn.dataset.fav === 'true';
        const endpoint = isFav ? '/Barbershops/RemoveFavourite' : '/Barbershops/SaveFavourite';

        try {
            const resp = await fetch(endpoint, {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': window.BarberLocConfig.antiForgeryToken
                },
                body: JSON.stringify({
                    placeId: currentPlaceId,
                    placeName: d.name,
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

    function updateFavBtn(isFav) {
        newBtn.dataset.fav = isFav ? 'true' : 'false';
        const ic = newBtn.querySelector('i');
        if (ic) {
            ic.className = isFav ? 'fas fa-heart' : 'far fa-heart';
        }
        newBtn.title = isFav ? 'Remover dos Favoritos' : 'Guardar nos Favoritos';
        newBtn.classList.toggle('btn-danger', isFav);
        newBtn.classList.toggle('btn-outline-danger', !isFav);
    }
}

// ── Panel state machine ───────────────────────────────────────────────────────
function setPanelState(state) {
    el('panel-loading').classList.toggle('d-none', state !== 'loading');
    el('panel-error').classList.toggle('d-none', state !== 'error');
    el('panel-content').classList.toggle('d-none', state !== 'content');
}

// ── Filter panel setup ────────────────────────────────────────────────────────
function setupFilterPanel() {
    // Category / rating filter buttons
    document.querySelectorAll('.filter-btn').forEach(btn => {
        btn.addEventListener('click', () => {
            const filter = btn.dataset.filter;
            const value = btn.dataset.value;

            // Toggle active state in group
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
    const sidebar = el('filter-sidebar');
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
        if (activeFilters.category && p.category !== activeFilters.category) return false;
        if (activeFilters.minRating && p.rating < parseFloat(activeFilters.minRating)) return false;
        if (activeFilters.mobileOnly && !p.hasMobile) return false;
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
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#039;');
}