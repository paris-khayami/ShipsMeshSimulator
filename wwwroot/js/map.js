"use strict";

// How ships are drawn: a hull points along the ship's course, and a dot marks a stopped ship.
const GLYPH = `<svg class="ship-glyph" viewBox="0 0 28 28" aria-hidden="true">
  <path class="hull" d="M14 3 L21 24 L14 20 L7 24 Z"/>
  <circle class="moored" cx="14" cy="14" r="5"/>
</svg>`;

const SHORE_GLYPH = `<svg class="ship-glyph" viewBox="0 0 28 28" aria-hidden="true">
  <rect class="shore" x="7" y="7" width="14" height="14"/>
</svg>`;

// AIS uses these values to mean "not available".
const SPEED_UNAVAILABLE = 102;
const COURSE_UNAVAILABLE = 360;
const MOVING_SPEED_KNOTS = 1;
const STALE_AFTER_SECONDS = 120;
const LINK_COLOR = "#b0127a";
const ROUTE_COLOR = "#12324a";
const MESSAGE_COLOR = "#c4122f";
const CLEAR_MESSAGE_AFTER_MS = 6000;

const map = L.map("map");
L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
  maxZoom: 19,
  attribution: '&copy; <a href="https://www.openstreetmap.org/copyright">OpenStreetMap</a> contributors',
}).addTo(map);

// Lines are drawn in this group so they can all be replaced together.
const linkLayer = L.layerGroup().addTo(map);
const routeLayer = L.layerGroup().addTo(map);
const messageLayer = L.layerGroup().addTo(map);

// Everything the page currently knows about, looked up by MMSI.
const markers = new Map();
const rows = new Map();
let ships = new Map();
let selectedMmsi = null;
let connected = false;
let listOrder = "";
let links = [];
let shore = null;
let shoreMarker = null;
let routes = new Map();
let currentMessageId = null;
let clearMessageTimer = null;

const element = (id) => document.getElementById(id);

const displayName = (ship) => ship.name || "(no name)";

function isMoving(ship) {
  return ship.speedKnots >= MOVING_SPEED_KNOTS
    && ship.speedKnots < SPEED_UNAVAILABLE
    && ship.courseDegrees < COURSE_UNAVAILABLE;
}

function formatSpeed(ship) {
  return ship.speedKnots < SPEED_UNAVAILABLE ? `${ship.speedKnots.toFixed(1)} kn` : "Unknown";
}

function formatCourse(ship) {
  return ship.courseDegrees < COURSE_UNAVAILABLE ? `${ship.courseDegrees.toFixed(0)}°` : "Unknown";
}

function secondsSince(time) {
  return Math.max(0, Math.round((Date.now() - new Date(time).getTime()) / 1000));
}

// Create or move one ship's marker.
function updateMarker(ship) {
  let marker = markers.get(ship.mmsi);

  if (!marker) {
    marker = L.marker([ship.latitude, ship.longitude], {
      icon: L.divIcon({ className: "ship-marker", html: GLYPH, iconSize: [28, 28], iconAnchor: [14, 14] }),
      title: displayName(ship),
    }).addTo(map);
    marker.on("click", () => selectShip(ship.mmsi));
    markers.set(ship.mmsi, marker);
  } else {
    marker.setLatLng([ship.latitude, ship.longitude]);
  }

  const markerElement = marker.getElement();
  markerElement.classList.toggle("is-stopped", !isMoving(ship));
  markerElement.classList.toggle("is-selected", ship.mmsi === selectedMmsi);
  markerElement.querySelector(".ship-glyph")
    .style.setProperty("--course", `${isMoving(ship) ? ship.courseDegrees : 0}deg`);
}

// Create or update one ship's row in the list. Ship names come from outside this app,
// so they're always set with textContent, which can't be interpreted as HTML.
function updateRow(ship) {
  let row = rows.get(ship.mmsi);

  if (!row) {
    row = document.createElement("li");
    const button = document.createElement("button");
    button.type = "button";
    button.className = "ship-row";
    const name = document.createElement("span");
    const speed = document.createElement("span");
    speed.className = "ship-row-speed";
    button.append(name, speed);
    button.addEventListener("click", () => {
      selectShip(ship.mmsi);
      const current = ships.get(ship.mmsi);
      if (current) {
        map.panTo([current.latitude, current.longitude]);
      }
    });
    row.append(button);
    rows.set(ship.mmsi, row);
  }

  const button = row.firstElementChild;
  button.setAttribute("aria-pressed", String(ship.mmsi === selectedMmsi));
  button.children[0].textContent = displayName(ship);
  button.children[1].textContent = formatSpeed(ship);
}

function removeShip(mmsi) {
  markers.get(mmsi)?.remove();
  markers.delete(mmsi);
  rows.get(mmsi)?.remove();
  rows.delete(mmsi);
  if (selectedMmsi === mmsi) {
    selectedMmsi = null;
  }
}

// The shore station never moves, so its marker is only created once.
function updateShoreMarker() {
  if (shoreMarker) {
    return;
  }

  shoreMarker = L.marker([shore.latitude, shore.longitude], {
    icon: L.divIcon({ className: "ship-marker", html: SHORE_GLYPH, iconSize: [28, 28], iconAnchor: [14, 14] }),
    title: shore.name,
  }).addTo(map);
}

// Finds where a link's end is, whether that's a ship or the shore station.
function nodePosition(id) {
  const node = id === shore.id ? shore : ships.get(id);
  return node ? [node.latitude, node.longitude] : null;
}

// Redraw every link. A stronger signal gets a thicker, more solid line.
function renderLinks() {
  linkLayer.clearLayers();

  for (const link of links) {
    const from = nodePosition(link.nodeA);
    const to = nodePosition(link.nodeB);
    if (!from || !to) {
      continue;
    }

    const isSelected = link.nodeA === selectedMmsi || link.nodeB === selectedMmsi;
    const strength = link.signalPercent / 100;
    L.polyline([from, to], {
      color: LINK_COLOR,
      weight: isSelected ? 4 : 1 + 2 * strength,
      opacity: isSelected ? 1 : 0.3 + 0.5 * strength,
      interactive: false,
    }).addTo(linkLayer);
  }
}

// Follow each ship's "next hop" to draw the route the selected ship would use right now.
function renderRoute() {
  routeLayer.clearLayers();

  const points = [];
  const visited = new Set();
  let id = selectedMmsi;

  while (id !== null && id !== undefined && !visited.has(id)) {
    const position = nodePosition(id);
    if (!position) {
      break;
    }
    points.push(position);
    visited.add(id);
    id = routes.get(id)?.nextHopId;
  }

  if (points.length > 1) {
    L.polyline(points, { color: ROUTE_COLOR, weight: 4, dashArray: "8 8", interactive: false }).addTo(routeLayer);
  }
}

function nodeName(id) {
  if (id === shore?.id) {
    return shore.name;
  }
  const ship = ships.get(id);
  return ship ? displayName(ship) : `Ship ${id}`;
}

function setMessageStatus(text, state) {
  const status = element("message-status");
  status.textContent = text;
  status.dataset.state = state;
}

function clearMessageLater() {
  clearTimeout(clearMessageTimer);
  clearMessageTimer = setTimeout(() => messageLayer.clearLayers(), CLEAR_MESSAGE_AFTER_MS);
}

function onMessageHop(hop) {
  // A message from another open page, or an older one, shouldn't confuse this display.
  if (currentMessageId === null) {
    currentMessageId = hop.messageId;
  }
  if (hop.messageId !== currentMessageId) {
    return;
  }

  const from = nodePosition(hop.fromId);
  const to = nodePosition(hop.toId);
  if (from && to) {
    L.polyline([from, to], { color: MESSAGE_COLOR, weight: 6, interactive: false }).addTo(messageLayer);
  }
  setMessageStatus(`Passing from ${nodeName(hop.fromId)} to ${nodeName(hop.toId)}`, "sending");
}

function onMessageDelivered(result) {
  if (result.messageId !== currentMessageId) {
    return;
  }

  const hops = result.path.length - 1;
  const route = result.path.map(nodeName).join(", then ");
  setMessageStatus(`Delivered in ${hops} ${hops === 1 ? "hop" : "hops"}: ${route}`, "delivered");
  currentMessageId = null;
  clearMessageLater();
}

function onMessageDropped(result) {
  if (currentMessageId !== null && result.messageId !== currentMessageId) {
    return;
  }

  setMessageStatus(`Not delivered. ${result.reason}`, "failed");
  currentMessageId = null;
  clearMessageLater();
}

async function sendMessage() {
  clearTimeout(clearMessageTimer);
  messageLayer.clearLayers();
  currentMessageId = null;
  setMessageStatus(`Sending from ${nodeName(selectedMmsi)}`, "sending");

  try {
    const accepted = await connection.invoke("SendTestMessage", selectedMmsi);
    if (!accepted) {
      setMessageStatus("Not sent. This ship is no longer on the map.", "failed");
    }
  } catch {
    setMessageStatus("Not sent. The connection to the server was lost.", "failed");
  }
}

function countLinks(mmsi) {
  return links.filter((link) => link.nodeA === mmsi || link.nodeB === mmsi).length;
}

function renderSelection() {
  const ship = ships.get(selectedMmsi);
  element("selected").hidden = !ship;
  if (!ship) {
    return;
  }

  element("selected-name").textContent = displayName(ship);
  element("selected-mmsi").textContent = ship.mmsi;
  element("selected-speed").textContent = formatSpeed(ship);
  element("selected-course").textContent = formatCourse(ship);
  element("selected-position").textContent = `${ship.latitude.toFixed(4)}, ${ship.longitude.toFixed(4)}`;
  element("selected-links").textContent = countLinks(ship.mmsi);

  const hops = routes.get(ship.mmsi)?.hopsToShore;
  element("selected-route").textContent = hops === null || hops === undefined
    ? "No route"
    : `${hops} ${hops === 1 ? "hop" : "hops"}`;
}

function selectShip(mmsi) {
  selectedMmsi = mmsi;
  ships.forEach((ship) => {
    updateMarker(ship);
    updateRow(ship);
  });
  renderLinks();
  renderRoute();
  renderSelection();
}

function renderStatus() {
  const status = element("connection");
  const times = [...ships.values()].map((ship) => secondsSince(ship.lastSeenUtc));
  const latest = times.length > 0 ? Math.min(...times) : null;

  element("latest-report").textContent = latest === null ? "" : `Latest report ${latest} s ago`;

  if (!connected) {
    status.dataset.state = "connecting";
    status.textContent = "Reconnecting to the server";
  } else if (latest !== null && latest > STALE_AFTER_SECONDS) {
    status.dataset.state = "stale";
    status.textContent = "Ship feed has gone quiet";
  } else {
    status.dataset.state = "live";
    status.textContent = "Live";
  }
}

// Runs each time the server sends the current list of ships.
function applyUpdate(update) {
  const next = new Map(update.ships.map((ship) => [ship.mmsi, ship]));

  ships.forEach((_, mmsi) => {
    if (!next.has(mmsi)) {
      removeShip(mmsi);
    }
  });

  ships = next;
  links = update.links;
  shore = update.shore;
  routes = new Map(update.routes.map((route) => [route.nodeId, route]));
  updateShoreMarker();
  renderLinks();
  renderRoute();

  ships.forEach((ship) => {
    updateMarker(ship);
    updateRow(ship);
  });

  // Moving rows around would lose keyboard focus, so only reorder when the order has changed.
  const sorted = [...ships.values()].sort((a, b) => displayName(a).localeCompare(displayName(b)));
  const order = sorted.map((ship) => ship.mmsi).join();
  if (order !== listOrder) {
    element("ship-list").replaceChildren(...sorted.map((ship) => rows.get(ship.mmsi)));
    listOrder = order;
  }

  element("empty-message").hidden = ships.size > 0;
  element("ship-count").textContent = ships.size === 0
    ? "No ships yet."
    : `Showing ${ships.size} of ${update.totalTracked} ships, moving ships first.`;
  element("link-count").textContent =
    `${links.length} simulated radio links, with a range of ${update.radioRangeKm} km.`;

  renderSelection();
  renderStatus();
}

// Center the map on the area we asked the feed for.
async function showMapArea() {
  try {
    const response = await fetch("api/map-area");
    const area = await response.json();
    map.fitBounds([[area.minLatitude, area.minLongitude], [area.maxLatitude, area.maxLongitude]]);
  } catch {
    map.setView([53.7, 9.35], 9); // The Elbe, if the area can't be loaded.
  }
}

// SignalR keeps a live connection to the server, which sends a new list every few seconds.
const connection = new signalR.HubConnectionBuilder()
  .withUrl("hubs/ships")
  .withAutomaticReconnect({
    // Keep trying forever, waiting up to 30 seconds between attempts.
    nextRetryDelayInMilliseconds: (context) => Math.min(1000 * 2 ** context.previousRetryCount, 30000),
  })
  .build();

connection.on("ShipsUpdated", applyUpdate);
connection.on("MessageHop", onMessageHop);
connection.on("MessageDelivered", onMessageDelivered);
connection.on("MessageDropped", onMessageDropped);

function setConnected(value) {
  connected = value;
  renderStatus();
}

connection.onreconnecting(() => setConnected(false));
connection.onreconnected(() => setConnected(true));

async function startConnection() {
  try {
    await connection.start();
    setConnected(true);
  } catch {
    setConnected(false);
    setTimeout(startConnection, 5000);
  }
}

element("clear-selection").addEventListener("click", () => selectShip(null));
element("send-message").addEventListener("click", sendMessage);
map.on("click", () => selectShip(null));

// Keep "Latest report" counting even between updates.
setInterval(renderStatus, 1000);

showMapArea();
startConnection();
