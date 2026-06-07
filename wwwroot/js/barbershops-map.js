// BarberLoc map JS
let map;
let infowindow;
let placesService;
let placesMarkers = [];
let serverMarkers = [];
let userMarker = null;
let accuracyCircle = null;
let geoWatchId = null;
let hasUserInteracted = false;

// 1. Esta função é chamada pelo Google Maps API através do callback na tua View
window.initMap = function () {
    console.log("Mapa a inicializar...");

    const portugal = { lat: 38.7223, lng: -9.1393 }; // Lisboa
    map = new google.maps.Map(document.getElementById("map"), {
        zoom: 12,
        center: portugal
    });

    infowindow = new google.maps.InfoWindow();
    placesService = new google.maps.places.PlacesService(map);

    // Iniciar a lógica do mapa
    setupUIListeners();
    if (navigator && navigator.geolocation) { locateUser(); }

    // Carregar as tuas barbearias
    loadBarbershops(map);
};

// 2. Esta função é chamada pela View (Map.cshtml)
function loadBarbershops(map) {
    console.log("A carregar barbearias do servidor...");
    searchPlacesInBounds();
}

// --- Funções originais mantidas ---

function clearMarkers() {
    placesMarkers.forEach(m => m.setMap(null));
    placesMarkers = [];
    serverMarkers.forEach(s => { if (s.marker) s.marker.setMap(null); });
    serverMarkers = [];
}

async function searchPlacesInBounds() {
    const loading = document.getElementById('mapLoading');
    if (loading) loading.style.display = 'block';

    clearMarkers();

    try {
        const resp = await fetch('/Barbershops/GetMapData', { credentials: 'same-origin' });
        if (!resp.ok) throw new Error('Erro no servidor');

        const places = await resp.json();

        places.forEach(p => {
            if (typeof p.lat !== 'number' || typeof p.lng !== 'number') return;
            const pos = new google.maps.LatLng(p.lat, p.lng);
            const marker = new google.maps.Marker({ position: pos, map: map, title: p.name });

            marker.addListener('click', () => {
                // Aqui podes chamar as tuas funções de detalhe
                console.log("Clicaste na barbearia: " + p.name);
            });

            serverMarkers.push({ marker: marker, data: p });
        });
    } catch (ex) {
        console.error('Erro ao carregar barbearias:', ex);
    } finally {
        if (loading) loading.style.display = 'none';
    }
}

function setupUIListeners() {
    const locateBtn = document.getElementById('locateBtn');
    if (locateBtn) {
        locateBtn.addEventListener('click', () => { locateUser(); });
    }
}

function locateUser() {
    if (!navigator.geolocation) return;
    navigator.geolocation.getCurrentPosition((pos) => {
        const latLng = new google.maps.LatLng(pos.coords.latitude, pos.coords.longitude);
        map.setCenter(latLng);
        map.setZoom(14);
    });
}