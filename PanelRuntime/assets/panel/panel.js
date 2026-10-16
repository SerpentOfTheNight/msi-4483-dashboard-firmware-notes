const ids = {
  pageKicker: document.getElementById("pageKicker"),
  pageTitle: document.getElementById("pageTitle"),
  backButton: document.getElementById("backButton"),
  homeView: document.getElementById("homeView"),
  detailView: document.getElementById("detailView"),
  detailSubtitle: document.getElementById("detailSubtitle"),
  detailPrimary: document.getElementById("detailPrimary"),
  detailPrimaryLabel: document.getElementById("detailPrimaryLabel"),
  detailMetrics: document.getElementById("detailMetrics"),
  detailList: document.getElementById("detailList"),
  detailFooterLeft: document.getElementById("detailFooterLeft"),
  detailUpdated: document.getElementById("detailUpdated"),
  cpuTemp: document.getElementById("cpuTemp"),
  cpuLoad: document.getElementById("cpuLoad"),
  cpuClock: document.getElementById("cpuClock"),
  cpuPower: document.getElementById("cpuPower"),
  gpuTemp: document.getElementById("gpuTemp"),
  gpuLoad: document.getElementById("gpuLoad"),
  gpuClockHome: document.getElementById("gpuClockHome"),
  gpuPowerHome: document.getElementById("gpuPowerHome"),
  network: document.getElementById("network"),
  networkName: document.getElementById("networkName"),
  networkLink: document.getElementById("networkLink"),
  networkRx: document.getElementById("networkRx"),
  networkTx: document.getElementById("networkTx"),
  caseFan: document.getElementById("caseFan"),
  pumpHome: document.getElementById("pumpHome"),
  memory: document.getElementById("memory"),
  memoryUsed: document.getElementById("memoryUsed"),
  memoryTotal: document.getElementById("memoryTotal"),
  memorySpeed: document.getElementById("memorySpeed"),
  storageUsage: document.getElementById("storageUsage"),
  storageTemp: document.getElementById("storageTemp"),
  storageFree: document.getElementById("storageFree"),
  storageModel: document.getElementById("storageModel"),
  sensorCount: document.getElementById("sensorCount"),
  updated: document.getElementById("updated")
};

const pages = {
  home: { title: "MSI GODLIKE", kicker: "" },
  cpu: { title: "CPU", kicker: "Processor", primaryLabel: "CPU Temperature" },
  gpu: { title: "GPU", kicker: "Graphics", primaryLabel: "GPU Temperature" },
  power: { title: "Power", kicker: "Package", primaryLabel: "CPU Package" },
  network: { title: "Network", kicker: "Adapters", primaryLabel: "Download Rate" },
  memory: { title: "Memory", kicker: "RAM", primaryLabel: "Memory Use" },
  cooling: { title: "Cooling", kicker: "Fans + Thermals", primaryLabel: "Radiator Fan" },
  media: { title: "MEDIA", kicker: "Gallery", primaryLabel: "" },
  nowplaying: { title: "Now Playing", kicker: "Media", primaryLabel: "" },
  board: { title: "Storage", kicker: "Drives", primaryLabel: "Primary Drive Used" },
  weather:{ title: "Weather", kicker: "Local", primaryLabel: "" },
  news: { title: "News", kicker: "RSS", primaryLabel: "" }
};

let homePageIndex = 0;
const HOME_PAGE_COUNT = 2;

function setHomePage(index) {
  homePageIndex = Math.max(0, Math.min(HOME_PAGE_COUNT - 1, index));

  document.querySelectorAll(".homePage").forEach((el) => {
    const n = Number(el.dataset.homePage);
    el.hidden = n !== homePageIndex;
  });

  document.querySelectorAll(".homeDot").forEach((dot) => {
    const n = Number(dot.dataset.homePage);
    dot.classList.toggle("active", n === homePageIndex);
  });

  notifyRendered();
}

document.getElementById("homeDots")?.addEventListener("click", (ev) => {
  const btn = ev.target.closest("[data-home-page]");
  if (!btn) return;
  ev.preventDefault();
  ev.stopPropagation();
  setHomePage(Number(btn.dataset.homePage));
  notifyActivity();
});

// swipe on home
(function bindHomeSwipe() {
  const host = document.querySelector(".homePages");
  if (!host) return;

  let startX = 0;
  let startY = 0;
  let tracking = false;

  host.addEventListener("pointerdown", (e) => {
    if (currentPage !== "home") return;
    tracking = true;
    startX = e.clientX;
    startY = e.clientY;
  }, { passive: true });

  host.addEventListener("pointerup", (e) => {
    if (!tracking || currentPage !== "home") return;
    tracking = false;
    const dx = e.clientX - startX;
    const dy = e.clientY - startY;
    if (Math.abs(dx) < 40 || Math.abs(dx) < Math.abs(dy)) return; // ignore taps / vertical scroll
    if (dx < 0) setHomePage(homePageIndex + 1);
    else setHomePage(homePageIndex - 1);
    notifyActivity();
  }, { passive: true });
})();


let snapshot = null;
let networkSnapshot = null;
let storageSnapshot = null;
let currentPage = "home";
let lastActivityPost = 0;
let lastRouteAt = 0;
let lastBestNetworkKey = null;
const CELSIUS = "℃";

const TONE_COLORS = {
  good: "#7dff95",
  warn: "#ffc046",
  hot: "#ff7a6e",
  neutral: "#7fd0ff"
};

const TONE_COLORS_LIGHT = {
  good: "#1e9b50",
  warn: "#cb6900",
  hot: "#c52a2a",
  neutral: "#1f6cc7"
};

function currentToneColors() {
  return document.body.dataset.theme === "light" ? TONE_COLORS_LIGHT : TONE_COLORS;
}

function createSparkline(canvas, capacity = 60) {
  if (!canvas) {
    return null;
  }
  const ctx = canvas.getContext("2d");
  const values = [];
  let strokeColor = TONE_COLORS.neutral;

  function draw() {
    const w = canvas.width;
    const h = canvas.height;
    ctx.clearRect(0, 0, w, h);
    if (values.length < 2) {
      return;
    }
    const min = Math.min(...values);
    const max = Math.max(...values);
    const range = Math.max(max - min, 1);
    const padTop = 4;
    const padBottom = 2;
    const drawH = h - padTop - padBottom;
    const start = capacity - values.length;

    ctx.beginPath();
    values.forEach((v, i) => {
      const x = ((start + i) / (capacity - 1)) * w;
      const y = padTop + drawH - ((v - min) / range) * drawH;
      if (i === 0) ctx.moveTo(x, y);
      else ctx.lineTo(x, y);
    });
    ctx.lineWidth = 1.6;
    ctx.lineCap = "round";
    ctx.lineJoin = "round";
    ctx.strokeStyle = strokeColor;
    ctx.stroke();

    const last = values[values.length - 1];
    const lastIndex = start + values.length - 1;
    const lastX = (lastIndex / (capacity - 1)) * w;
    const lastY = padTop + drawH - ((last - min) / range) * drawH;
    ctx.beginPath();
    ctx.arc(Math.min(lastX, w - 2), lastY, 1.8, 0, Math.PI * 2);
    ctx.fillStyle = strokeColor;
    ctx.fill();
  }

  return {
    push(value) {
      if (!Number.isFinite(value)) return;
      values.push(value);
      while (values.length > capacity) values.shift();
      draw();
    },
    setColor(color) {
      if (color && color !== strokeColor) {
        strokeColor = color;
        draw();
      }
    }
  };
}

function tempTone(value) {
  if (!Number.isFinite(value)) return "neutral";
  if (value >= 80) return "hot";
  if (value >= 60) return "warn";
  return "good";
}

function setTone(card, tone) {
  if (!card) return;
  if (card.dataset.tone !== tone) {
    card.dataset.tone = tone;
  }
}

document.body.dataset.page = "home";

const cards = {
  cpu: document.getElementById("cpuTemp")
    ? document.getElementById("cpuTemp").closest(".summaryCard")
    : null,
  gpu: document.getElementById("gpuTemp")
    ? document.getElementById("gpuTemp").closest(".summaryCard")
    : null
};

const sparks = {
  cpuTemp: createSparkline(document.getElementById("cpuTempSpark")),
  gpuTemp: createSparkline(document.getElementById("gpuTempSpark"))
};

window.addEventListener("aida64-sensors", event => {
  snapshot = event.detail;
  pushSparkValues();
  render();
});

function pushSparkValues() {
  if (!snapshot || !snapshot.sensors) return;
  const cpuTemp = pick("TCPUPKG", "TCPU", "TCPU1") || pickByRegex(/cpu.*temp|tcpu/i);
  const gpuTemp = pick("TGPU1", "TGPU") || pickByRegex(/gpu.*temp/i);
  if (sparks.cpuTemp) sparks.cpuTemp.push(number(cpuTemp));
  if (sparks.gpuTemp) sparks.gpuTemp.push(number(gpuTemp));
}

window.addEventListener("network-snapshot", event => {
  networkSnapshot = event.detail;
  renderNetwork();
  renderDetail();
});

window.addEventListener("storage-snapshot", event => {
  storageSnapshot = event.detail;
  renderStorage();
  renderDetail();
});

let mediaList = { host: "", intervalMs: 8000, files: [] };
let mediaIndex = 0;
let mediaTimer = null;

const mediaIds = {
  view: document.getElementById("mediaView"),
  image: document.getElementById("mediaImage"),
  video: document.getElementById("mediaVideo"),
  empty: document.getElementById("mediaEmpty"),
  name: document.getElementById("mediaName"),
  counter: document.getElementById("mediaCounter"),
  countHome: document.getElementById("mediaCountHome")
};

window.addEventListener("media-list", event => {
  mediaList = event.detail || { host: "", intervalMs: 8000, files: [] };
  showCurrentMedia();
  mediaIndex = 0;
  if (mediaIds.countHome) {
    DispatchMediaListAsync();
    mediaIds.countHome.textContent = mediaList.files.length || "0";
  }
  if (currentPage === "media") {
    DispatchMediaListAsync();
    showCurrentMedia();
  }
});

function isVideo(name) {
  return /\.(mp4|webm|mov)$/i.test(name);
}

function clearMediaTimer() {
  if (mediaTimer) {
    clearTimeout(mediaTimer);
    mediaTimer = null;
  }
}


function showCurrentMedia() {
  clearMediaTimer();
  const files = mediaList.files || [];
  if (!files.length) {
    mediaIds.image.hidden = true;
    mediaIds.video.hidden = true;
    mediaIds.empty.hidden = false;
    mediaIds.name.textContent = "No media";
    mediaIds.counter.textContent = "0 / 0";
    return;
  }

  mediaIds.empty.hidden = true;
  const name = files[mediaIndex % files.length];
  const url = mediaList.host + name;

  mediaIds.name.textContent = name;
  mediaIds.counter.textContent = `${(mediaIndex % files.length) + 1} / ${files.length}`;

  if (isVideo(name)) {
    mediaIds.image.hidden = true;
    mediaIds.video.hidden = false;
    mediaIds.video.src = url;
    mediaIds.video.onended = () => {
      mediaIndex = (mediaIndex + 1) % files.length;
      showCurrentMedia();
    };
    mediaIds.video.play().catch(() => {});
    startMediaActivityBoost();
  } else {
    stopMediaActivityBoost();
    mediaIds.video.pause();
    mediaIds.video.removeAttribute("src");
    mediaIds.video.hidden = true;
    mediaIds.image.hidden = false;
    mediaIds.image.src = url;
    mediaTimer = setTimeout(() => {
      mediaIndex = (mediaIndex + 1) % files.length;
      showCurrentMedia();
    }, mediaList.intervalMs || 8000);
  }
  notifyRendered();
}

let mediaActivityTimer = null;

function startMediaActivityBoost() {
  stopMediaActivityBoost();
  // existing path: host listens for messages containing "activity"
  const ping = () => {
    try {
      chrome.webview.postMessage("activity");
    } catch (_) {
      // ignore if not in WebView2
    }
  };
  ping();
  mediaActivityTimer = setInterval(ping, 2000); // every 2s while video plays
}

function stopMediaActivityBoost() {
  if (mediaActivityTimer) {
    clearInterval(mediaActivityTimer);
    mediaActivityTimer = null;
  }
}






// extend openPage
const originalOpenPage = openPage;
openPage = function(page) {
  originalOpenPage(page);
  if (page === "media") {
    ids.homeView.hidden = true;
    ids.detailView.hidden = true;
    mediaIds.view.hidden = false;
    showCurrentMedia();
  } else {
    if(page =="nowplaying")
    {
      renderNowPlaying();
    }else
    {
      mediaIds.view.hidden = true;
      clearMediaTimer();
      mediaIds.video.pause();
    }
  }
};

// pages map
pages.nowplaying = { title: "Now Playing", kicker: "Media", primaryLabel: "" };

let mediaSession = null;
let npSync = {
  position: 0,
  duration: 0,
  playing: false,
  syncedAt: 0,
  lastReportedPos: -1,
  trackKey: ""
};

function getLivePosition() {
  if (!npSync.playing || !npSync.syncedAt) return npSync.position;
  const elapsed = (performance.now() - npSync.syncedAt) / 1000;
  const pos = npSync.position + elapsed;
  return npSync.duration > 0 ? Math.min(pos, npSync.duration) : pos;
}

function applyMediaSession(detail) {
  mediaSession = detail;

  const newPos = Number(detail.positionSeconds) || 0;
  const newDur = Number(detail.durationSeconds) || 0;
  const playing = String(detail.status || "").toLowerCase() === "playing";
  const trackKey = [
    detail.title || "",
    detail.artist || "",
    detail.appName || "",
    detail.album || ""
  ].join("|");

  const statusChanged = playing !== npSync.playing;
  const trackChanged = trackKey !== npSync.trackKey;
  const first = npSync.syncedAt === 0 && npSync.trackKey === "";
  const reportedChanged =
    npSync.lastReportedPos < 0 ||
    Math.abs(newPos - npSync.lastReportedPos) > 0.5;

  // Always take server duration (0 clears Chrome's leftover)
  npSync.duration = newDur > 0 ? newDur : 0;
  npSync.playing = playing;
  npSync.trackKey = trackKey;

  if (trackChanged || first || statusChanged || !playing || reportedChanged) {
    npSync.position = newPos;
    npSync.lastReportedPos = newPos;
    // only run the local clock when we have a real timeline
    npSync.syncedAt = npSync.duration > 0 && playing ? performance.now() : 0;
  }

  // No timeline (foobar, etc.) — hard clear so UI hides and times don't stick
  if (npSync.duration <= 0) {
    npSync.position = 0;
    npSync.syncedAt = 0;
    npSync.lastReportedPos = newPos;
  }

  renderNowPlaying();
  updateNowPlayingProgress();
}

function updateNowPlayingProgress() {
  if (currentPage !== "nowplaying") return;

  const pos = getLivePosition();
  const dur = npSync.duration || 0;
  const pct = dur > 0 ? Math.min(100, (pos / dur) * 100) : 0;
  const hasTimeline = dur > 0;

  // id → getElementById("...")  (no dot)
  // class → querySelector("....")
  const bar = document.getElementById("npProgressBar");
  const barWrap = document.querySelector(".npProgressWrap");
  const times = document.querySelector(".npTimes");
  const posEl = document.getElementById("npPos");
  const durEl = document.getElementById("npDur");

  if (bar) bar.style.width = hasTimeline ? pct + "%" : "0%";
  if (posEl) posEl.textContent = hasTimeline ? formatTime(pos) : "--";
  if (durEl) durEl.textContent = hasTimeline ? formatTime(dur) : "--";

  // hide completely when foobar (etc.) sends no duration
  if (barWrap) barWrap.style.display = hasTimeline ? "" : "none";
  if (times) times.style.display = hasTimeline ? "" : "none";
}

window.addEventListener("media-session", (e) => applyMediaSession(e.detail || {}));

// local tick — this is what moves the bar between (sparse) SMTC updates
setInterval(updateNowPlayingProgress, 250);

function formatTime(sec) {
  sec = Math.max(0, Math.floor(sec || 0));
  const m = Math.floor(sec / 60);
  const s = sec % 60;
  return `${m}:${String(s).padStart(2, "0")}`;
}

function renderNowPlaying() {
  const online = mediaSession && mediaSession.online;
  const title = online ? mediaSession.title : "Nothing playing";
  const artist = online ? mediaSession.artist : "--";
  const album = online && mediaSession.album ? mediaSession.album : "";
  const status = online ? mediaSession.status : "Stopped";

  const homeTitle = document.getElementById("npHomeTitle");
  const homeArtist = document.getElementById("npHomeArtist");
  const homeStatus = document.getElementById("npHomeStatus");
  if (homeTitle) homeTitle.textContent = title;
  if (homeArtist) homeArtist.textContent = artist;
  if (homeStatus) homeStatus.textContent = status;

  if (currentPage !== "nowplaying") return;

  document.getElementById("npTitle").textContent = title;
  document.getElementById("npArtist").textContent = artist;
  document.getElementById("npAlbum").textContent = album || "\u00A0";
  document.getElementById("npStatus").textContent = status;

  const art = document.getElementById("npArt");
  const ph = document.getElementById("npArtPlaceholder");
  if (online && mediaSession.thumbnailDataUrl) {
    art.src = mediaSession.thumbnailDataUrl;
    art.hidden = false;
    ph.hidden = true;
  } else {
    art.removeAttribute("src");
    art.hidden = true;
    ph.hidden = false;
  }
  notifyRendered();
}

// buttons
document.querySelectorAll("[data-media-cmd]").forEach((btn) => {
  const fire = (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const cmd = btn.getAttribute("data-media-cmd");
    postWebMessage("media:" + cmd);
    notifyActivity();
  };
  //btn.addEventListener("pointerup", fire);
  btn.addEventListener("click", fire);
});

document.addEventListener("pointerdown", notifyActivity, { passive: true });
document.addEventListener("pointerup", notifyActivity, { passive: true });
document.addEventListener("keydown", notifyActivity);
document.addEventListener("pointermove", () => {
  const now = Date.now();
  if (now - lastActivityPost >= 250) {
    notifyActivity();
  }
}, { passive: true });

document.querySelectorAll("[data-page]").forEach(element => {
  element.addEventListener("pointerup", event => routeFromEvent(event, element));
  element.addEventListener("click", event => routeFromEvent(event, element));
});

function bindCommandButtons(selector, prefix) {
  document.querySelectorAll(selector).forEach((btn) => {
    btn.addEventListener("click", (ev) => {
      ev.preventDefault();
      ev.stopPropagation();
      const cmd = btn.getAttribute(selector.includes("volume") ? "data-volume-cmd" : "data-media-cmd");
      if (!cmd) return;
      postWebMessage(prefix + cmd);
      notifyActivity();
    });
  });
}

// volume
document.querySelectorAll("[data-volume-cmd]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    postWebMessage("volume:" + btn.getAttribute("data-volume-cmd"));
    notifyActivity();
  });
});

window.addEventListener("system-volume", (e) => {
  const { level, muted } = e.detail;
  const bar = document.getElementById("npVolBar");
  if (bar) bar.style.width = Math.round(level * 100) + "%";
  const muteBtn = document.getElementById("npMuteBtn");
  if (muteBtn) muteBtn.textContent = muted ? "🔈" : "🔇";
});

ids.backButton.addEventListener("pointerup", event => routeFromEvent(event, ids.backButton, "home"));
ids.backButton.addEventListener("click", event => routeFromEvent(event, ids.backButton, "home"));

pages.weather = { title: "Weather", kicker: "Local", primaryLabel: "" };
pages.news = { title: "News", kicker: "RSS", primaryLabel: "" };

let weatherSnapshot = null;
let rssSnapshot = null;
let rssPageIndex = 0;
let rssCycleTimer = null;

// in openPage toggles:
// weatherView.hidden = page !== "weather";
// newsView.hidden = page !== "news";

window.addEventListener("weather-snapshot", (e) => {
  weatherSnapshot = e.detail;
  renderWeather();
});


// How many cards fit on the panel without scrolling
const RSS_PAGE_SIZE = 4;
const RSS_CYCLE_MS = 12000; // 12s per page
window.addEventListener("rss-snapshot", (e) => {
  rssSnapshot = e.detail;
  rssPageIndex = 0;
  renderRss();
  restartRssCycle();
});

document.getElementById("newsView")?.addEventListener("pointerup", (ev) => {
  if (currentPage !== "news") return;
  if (rssPageCount() <= 1) return;
  if (ev.target.closest("footer, .themeToggle, button")) return;

  const view = document.getElementById("newsView");
  if (!view) return;

  const rect = view.getBoundingClientRect();
  const x = ev.clientX - rect.left;
  const mid = rect.width / 2;
  const pages = rssPageCount();

  if (x < mid) {
    rssPageIndex = (rssPageIndex - 1 + pages) % pages; // left → back
  } else {
    rssPageIndex = (rssPageIndex + 1) % pages;         // right → next
  }

  renderRssListOnly();
  restartRssCycle();
  notifyActivity();
  notifyRendered();
}, { passive: true });

function renderWeather() {
  const w = weatherSnapshot;
  const unit = w?.unit || "°F";
  const homeTemp = document.getElementById("wxHomeTemp");
  const homeCond = document.getElementById("wxHomeCond");
  const homeRange = document.getElementById("wxHomeRange");

  if (!w || !w.online) {
    if (homeTemp) homeTemp.textContent = "--";
    if (homeCond) homeCond.textContent = "Offline";
    if (homeRange) homeRange.textContent = "--";
  } else {
    if (homeTemp) homeTemp.textContent = Math.round(w.temp) + unit;
    if (homeCond) homeCond.textContent = w.condition || "--";
    const d0 = w.days && w.days[0];
    if (homeRange) {
      homeRange.textContent = d0
        ? `${Math.round(d0.high)}° / ${Math.round(d0.low)}°`
        : "--";
    }
  }

  if (currentPage !== "weather") return;
  if (!w || !w.online) return;

  document.getElementById("wxTemp").textContent = Math.round(w.temp) + unit;
  document.getElementById("wxCond").textContent = w.condition || "--";
  document.getElementById("wxHumidity").textContent =
    "Humidity " + Math.round(w.humidity) + "%";
  document.getElementById("wxWind").textContent =
    "Wind " + Math.round(w.wind);
  document.getElementById("wxUpdated").textContent = timeText(w.updatedAt);

  const box = document.getElementById("wxForecast");
  box.replaceChildren(...(w.days || []).map((d) => {
    const el = document.createElement("div");
    el.className = "wxDay";
    el.innerHTML =
      `<span>${d.label}</span>` +
      `<b>${Math.round(d.high)}°</b>` +
      `<span>${Math.round(d.low)}°</span>` +
      `<div>${d.condition || ""}</div>`;
    return el;
  }));
  notifyRendered();
}

function rssItems() {
  return rssSnapshot && rssSnapshot.online ? (rssSnapshot.items || []) : [];
}

function rssPageCount() {
  const n = rssItems().length;
  return n === 0 ? 1 : Math.ceil(n / RSS_PAGE_SIZE);
}

function restartRssCycle() {
  if (rssCycleTimer) {
    clearInterval(rssCycleTimer);
    rssCycleTimer = null;
  }
  if (rssPageCount() <= 1) return;
  rssCycleTimer = setInterval(() => {
    if (currentPage !== "news") return;
    rssPageIndex = (rssPageIndex + 1) % rssPageCount();
    renderRssListOnly();
    notifyRendered();
  }, RSS_CYCLE_MS);
}

function renderRss() {
  const items = rssItems();
  const countEl = document.getElementById("rssHomeCount");
  const titleEl = document.getElementById("rssHomeTitle");
  if (countEl) countEl.textContent = String(items.length || 0);
  if (titleEl) titleEl.textContent = items[0]?.title?.slice(0, 40) || "--";

  if (currentPage !== "news") return;

  const updated = document.getElementById("rssUpdated");
  if (updated) {
    updated.textContent = rssSnapshot?.updatedAt
      ? timeText(rssSnapshot.updatedAt)
      : "--";
  }
  renderRssListOnly();
  restartRssCycle();
  notifyRendered();
}

function renderRssListOnly() {
  const list = document.getElementById("rssList");
  const pager = document.getElementById("rssPager");
  if (!list) return;

  const items = rssItems();
  if (!items.length) {
    list.innerHTML =
      `<article class="rssItem"><div class="title">${
        rssSnapshot?.error || "No headlines"
      }</div></article>`;
    if (pager) pager.textContent = "";
    return;
  }

  const pages = rssPageCount();
  rssPageIndex = ((rssPageIndex % pages) + pages) % pages;
  const start = rssPageIndex * RSS_PAGE_SIZE;
  const slice = items.slice(start, start + RSS_PAGE_SIZE);

  list.replaceChildren(...slice.map((it) => {
    const el = document.createElement("article");
    el.className = "rssItem";

    const src = document.createElement("div");
    src.className = "src";
    src.textContent = it.source || "";

    const title = document.createElement("div");
    title.className = "title";
    title.textContent = it.title || "";

    const blurb = document.createElement("div");
    blurb.className = "blurb";
    blurb.textContent = it.summary || "";

    const when = document.createElement("div");
    when.className = "when";
    when.textContent = it.published || "";

    el.append(src, title);
    if (it.summary) el.append(blurb);
    el.append(when);
    return el;
  }));

  if (pager) {
    pager.textContent = pages > 1
      ? `${rssPageIndex + 1} / ${pages}`
      : "";
  }
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

function notifyActivity(message = "activity") {
  lastActivityPost = Date.now();
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(message);
  }
}

function postWebMessage(message) {
  if (window.chrome && window.chrome.webview) {
    window.chrome.webview.postMessage(message);
  }
}

function notifyRendered() {
  requestAnimationFrame(() => {
    requestAnimationFrame(() => postWebMessage("rendered"));
  });
}

function openPage(page) {
  if (!pages[page]) page = "home";

  currentPage = page;
  document.body.dataset.page = page;

  const homeView = document.getElementById("homeView");
  const detailView = document.getElementById("detailView");
  const mediaView = document.getElementById("mediaView");
  const nowPlayingView = document.getElementById("nowPlayingView");
  const weatherView = document.getElementById("weatherView");
  const newsView = document.getElementById("newsView");

  if (homeView) homeView.hidden = page !== "home";
  if (detailView) detailView.hidden =
    !(page !== "home" && page !== "media" && page !== "nowplaying" &&
      page !== "weather" && page !== "news");
  if (mediaView) mediaView.hidden = page !== "media";
  if (nowPlayingView) nowPlayingView.hidden = page !== "nowplaying";
  if (weatherView) weatherView.hidden = page !== "weather";
  if (newsView) newsView.hidden = page !== "news";

  ids.backButton.hidden = page === "home";
  ids.pageTitle.textContent = pages[page].title;
  ids.pageKicker.textContent = pages[page].kicker;

  if (page === "media" && typeof showCurrentMedia === "function") {
    showCurrentMedia();
  } else if (page === "nowplaying" && typeof renderNowPlaying === "function") {
    renderNowPlaying();
  } else if (page === "weather" && typeof renderWeather === "function") {
    renderWeather();          // paint from cached snapshot
  } else if (page === "news" && typeof renderRss === "function") {
    renderRss();
  } else if (page !== "home") {
    renderDetail();
  }

  notifyRendered();
}

function routeFromEvent(event, element, pageOverride = null) {
  event.preventDefault();
  event.stopPropagation();

  const now = Date.now();
  if (event.type === "click" && now - lastRouteAt < 350) {
    return;
  }

  lastRouteAt = now;
  openPage(pageOverride || element.dataset.page || "home");
}

function pick(...sensorIds) {
  if (!snapshot || !snapshot.sensors) {
    return null;
  }

  for (const id of sensorIds) {
    const value = snapshot.sensors[id];
    if (value) {
      return value;
    }
  }
  return null;
}

function allSensors() {
  return snapshot && snapshot.sensors ? Object.values(snapshot.sensors) : [];
}

function pickByRegex(regex, kind = null) {
  return allSensors().find(sensor => {
    if (kind && sensor.kind !== kind) {
      return false;
    }
    return regex.test(sensor.id || "") || regex.test(sensor.label || "");
  }) || null;
}

function pickFanByRegex(regex) {
  return allSensors().find(sensor => {
    if (sensor.kind !== "fan" && sensor.kind !== "cooler") {
      return false;
    }
    const id = sensor.id || "";
    const label = sensor.label || "";
    return regex.test(id) || regex.test(label);
  }) || null;
}

function collectIds(...sensors) {
  const set = new Set();
  for (const s of sensors) {
    if (s && s.id) set.add(s.id);
  }
  return set;
}

function rowsByRegex(regex, limit = 8, kind = null, excludeIds = null) {
  const exclude = excludeIds instanceof Set ? excludeIds : null;
  return allSensors()
    .filter(sensor => {
      if (kind && sensor.kind !== kind) return false;
      if (exclude && exclude.has(sensor.id)) return false;
      return regex.test(sensor.id || "") || regex.test(sensor.label || "");
    })
    .slice(0, limit)
    .map(sensor => ({ label: sensor.label || sensor.id, value: formatSensor(sensor) }));
}

function number(sensor) {
  return sensor && Number.isFinite(sensor.value) ? sensor.value : null;
}

function metric(sensor, suffix = "") {
  const value = number(sensor);
  if (value === null) {
    return sensor && sensor.raw ? `${sensor.raw}${suffix}` : "--";
  }
  return `${Math.round(value)}${suffix}`;
}

function sensorText(sensor) {
  if (!sensor) {
    return "--";
  }
  if (sensor.raw !== null && sensor.raw !== undefined && `${sensor.raw}`.length > 0) {
    return `${sensor.raw}`;
  }
  return number(sensor) === null ? "--" : `${Math.round(number(sensor))}`;
}

function numericText(value, digits = 0) {
  if (!Number.isFinite(value)) {
    return "--";
  }
  return value.toFixed(digits).replace(/\.0+$/, "").replace(/(\.\d*[1-9])0+$/, "$1");
}

function tempNumber(sensor) {
  const value = number(sensor);
  if (value === null) {
    return "--";
  }
  return `${Math.round(value)}`;
}

function formatTemp(sensor, withUnit = true) {
  const value = tempNumber(sensor);
  return withUnit && value !== "--" ? `${value}${CELSIUS}` : value;
}

function formatPercent(sensor) {
  const value = number(sensor);
  return value === null ? "--" : `${Math.round(value)}%`;
}

function formatClock(sensor, compact = false) {
  const value = number(sensor);
  if (value === null) {
    return "--";
  }
  if (compact && value >= 1000) {
    return `${numericText(value / 1000, 1)} GHz`;
  }
  return `${Math.round(value)} MHz`;
}

function formatCpuClock(sensor) {
  const value = number(sensor);
  if (value === null) {
    return "--";
  }
  return `${numericText(value / 1000, 2)} GHz`;
}

function formatPower(sensor) {
  const value = number(sensor);
  if (value === null) {
    return "--";
  }
  return `${numericText(value, value < 10 ? 1 : 0)} W`;
}

function formatVolt(sensor) {
  const value = number(sensor);
  return value === null ? "--" : `${numericText(value, 2)} V`;
}

function formatAmp(sensor) {
  const value = number(sensor);
  return value === null ? "--" : `${numericText(value, 2)} A`;
}

function formatRpm(sensor) {
  const value = number(sensor);
  return value === null ? "--" : `${Math.round(value)} RPM`;
}

function formatMemoryMb(sensor) {
  const value = number(sensor);
  if (value === null) {
    return sensorText(sensor);
  }
  if (value >= 1024) {
    return `${numericText(value / 1024, 1)} GB`;
  }
  return `${Math.round(value)} MB`;
}

function formatMb(mb) {
  if (!Number.isFinite(mb) || mb <= 0) return "--";
  if (mb >= 1024) return `${numericText(mb / 1024, 1)} GB`;
  return `${Math.round(mb)} MB`;
}

function formatSensor(sensor) {
  if (!sensor) {
    return "--";
  }

  const kind = sensor.kind || "";
  const id = sensor.id || "";
  const label = sensor.label || "";
  const text = `${id} ${label}`;

  if (kind === "fan" || kind === "cooler") {
    return formatRpm(sensor);
  }
  if (kind === "temp") {
    return formatTemp(sensor);
  }
  if (kind === "pwr" || kind === "power") {
    return formatPower(sensor);
  }
  if (kind === "volt") {
    return formatVolt(sensor);
  }
  if (kind === "curr") {
    return formatAmp(sensor);
  }

  if (/CLK|clock/i.test(text)) {
    return /cpu/i.test(text) ? formatCpuClock(sensor) : formatClock(sensor);
  }
  if (/TDP%|utili[sz]ation|\buti\b|usage|load/i.test(text) || /^D/i.test(id)) {
    return formatPercent(sensor);
  }
  if (/used.*memory|free.*memory|dedicated memory|dynamic memory|video memory|vram/i.test(label)) {
    return formatMemoryMb(sensor);
  }
  if (/temp|hotspot|water|coolant|diode|socket|core #|dimm/i.test(label) || /^T/i.test(id)) {
    return formatTemp(sensor);
  }
  if (/power|package|watt/i.test(label) || (/^P/i.test(id) && !/^PCH/i.test(id))) {
    return formatPower(sensor);
  }
  return sensorText(sensor);
}

function percent(sensor) {
  return metric(sensor, "%");
}

function setBar(fill, text, sensor) {
  const value = Math.max(0, Math.min(100, number(sensor) ?? 0));
  fill.style.width = `${value}%`;
  text.textContent = sensor ? `${Math.round(value)}%` : "--";
}

function setLevel(element, percent) {
  const host = element ? element.closest(".meterCard") : null;
  if (!host) {
    return;
  }
  const value = Math.max(0, Math.min(100, Number(percent) || 0));
  host.style.setProperty("--level", `${value}%`);
}

function setText(element, value) {
  if (!element) {
    return;
  }

  const text = value === null || value === undefined || value === "" ? "--" : `${value}`;
  if (element.textContent === text) {
    return;
  }

  element.textContent = text;
  element.classList.remove("valueChanged");
  void element.offsetWidth;
  element.classList.add("valueChanged");
}

function setUnitValue(element, value, unit) {
  if (!element) {
    return;
  }

  const text = value === null || value === undefined || value === "" ? "--" : `${value}`;
  const key = text === "--" ? text : `${text}${unit}`;
  if (element.dataset.valueText === key) {
    return;
  }

  element.dataset.valueText = key;
  if (text === "--") {
    element.replaceChildren(document.createTextNode("--"));
  }
  else {
    const unitSpan = document.createElement("span");
    unitSpan.className = "unitMark";
    unitSpan.textContent = unit;
    element.replaceChildren(document.createTextNode(text), unitSpan);
  }
  element.classList.remove("valueChanged");
  void element.offsetWidth;
  element.classList.add("valueChanged");
}

function bestNetworkAdapter() {
  if (!networkSnapshot || !networkSnapshot.adapters) {
    return null;
  }

  const up = Object.values(networkSnapshot.adapters)
    .filter(a => a && a.status === "Up");
  if (up.length === 0) return null;

  const rateOf = a => (a.rxBytesPerSec || 0) + (a.txBytesPerSec || 0);
  const withRate = up.filter(a => rateOf(a) > 0);

  if (withRate.length > 0) {
    const challenger = withRate.reduce((best, a) => rateOf(a) > rateOf(best) ? a : best);
    const sticky = lastBestNetworkKey
      ? withRate.find(a => a.key === lastBestNetworkKey)
      : null;
    const winner = sticky && challenger.key !== sticky.key && rateOf(challenger) > rateOf(sticky) * 2
      ? challenger
      : sticky || challenger;
    lastBestNetworkKey = winner.key;
    return winner;
  }

  return up.reduce((best, a) =>
    (a.linkSpeedBps || 0) > (best.linkSpeedBps || 0) ? a : best
  );
}

function diskTempSensors(limit = 6) {
  return allSensors()
    .filter(sensor => {
      const id = sensor.id || "";
      const label = sensor.label || "";
      return sensor.kind === "temp" && (/^THDD/i.test(id) || /kingston|wds|zhitai|ssd|hdd|nvme/i.test(label));
    })
    .slice(0, limit);
}

function storageVolumes() {
  return storageSnapshot && Array.isArray(storageSnapshot.volumes)
    ? storageSnapshot.volumes
    : [];
}

function storageDisks() {
  return storageSnapshot && Array.isArray(storageSnapshot.disks)
    ? storageSnapshot.disks.slice().sort((a, b) => {
      const aHasC = diskHasVolume(a, "C:");
      const bHasC = diskHasVolume(b, "C:");
      if (aHasC !== bHasC) {
        return aHasC ? -1 : 1;
      }
      return (Number(a.diskNumber) || 0) - (Number(b.diskNumber) || 0);
    })
    : [];
}

function diskHasVolume(disk, volumeName) {
  if (!disk || !Array.isArray(disk.volumes)) {
    return false;
  }

  return disk.volumes.some(volume => `${volume}`.toUpperCase() === volumeName.toUpperCase());
}

function primaryStorageDisk() {
  const disks = storageDisks();
  const cDisk = disks.find(disk => diskHasVolume(disk, "C:"));
  if (cDisk) {
    return cDisk;
  }

  if (disks.length > 0) {
    return disks[0];
  }

  const volumes = storageVolumes();
  if (volumes.length === 0) {
    return null;
  }

  return {
    diskNumber: "",
    model: "Windows Storage",
    health: "",
    volumes: volumes.map(volume => volume.name)
  };
}

function volumesForDisk(disk) {
  const volumes = storageVolumes();
  if (!disk) {
    return volumes;
  }

  if (Array.isArray(disk.volumes) && disk.volumes.length > 0) {
    const names = new Set(disk.volumes.map(name => `${name}`.toUpperCase()));
    return volumes.filter(volume => names.has(`${volume.name}`.toUpperCase()));
  }

  if (disk.diskNumber !== undefined && `${disk.diskNumber}`.length > 0) {
    return volumes.filter(volume => `${volume.diskNumber}` === `${disk.diskNumber}`);
  }

  return volumes;
}

function diskUsedPercent(disk) {
  const volumes = volumesForDisk(disk);
  const total = volumes.reduce((sum, volume) => sum + (volume.totalBytes || 0), 0);
  const used = volumes.reduce((sum, volume) => sum + (volume.usedBytes || 0), 0);
  return total > 0 ? used * 100 / total : 0;
}

function shortBytes(text) {
  return `${text || "--"}`
    .replace(/\.0(?=[GT]B)/g, "")
    .replace(/GB/g, "G")
    .replace(/TB/g, "T");
}

function volumeLetters(volumes) {
  return volumes.length > 0
    ? volumes.map(volume => volume.name || "--").join(" ")
    : "--";
}

function freeSummary(volumes, limit = 3) {
  if (!volumes || volumes.length === 0) {
    return "--";
  }

  const shown = volumes.slice(0, limit).map(volume => `${(volume.name || "").replace(":", "")} ${shortBytes(volume.freeText)}`);
  const suffix = volumes.length > limit ? ` +${volumes.length - limit}` : "";
  return `${shown.join(" ")}${suffix}`;
}

function shortDiskModel(value) {
  const text = typeof value === "string"
    ? value
    : value && (value.model || value.label || value.id)
      ? value.model || value.label || value.id
      : "--";
  return text
    .replace(/WDS100T1X0E-00AFY0/i, "WD SN850")
    .replace(/KINGSTON\s*/i, "Kingston ")
    .replace(/ZHITAI\s*/i, "ZhiTai ")
    .replace(/\s*#\d+$/i, "")
    .replace(/\s+/g, " ")
    .trim()
    .slice(0, 16);
}

function normalizeDiskText(value) {
  return `${value || ""}`
    .replace(/\s*#\d+$/i, "")
    .toLowerCase()
    .replace(/[^a-z0-9]/g, "");
}

function findDiskTemp(disk) {
  if (!disk) {
    return null;
  }

  const model = normalizeDiskText(disk.model);
  const shortModel = normalizeDiskText(shortDiskModel(disk));
  if (!model && !shortModel) {
    return null;
  }

  return diskTempSensors(20).find(sensor => {
    const label = normalizeDiskText(sensor.label || sensor.id);
    return (model && (label.includes(model) || model.includes(label)))
      || (shortModel && (label.includes(shortModel) || shortModel.includes(label)));
  }) || null;
}

function diskTempText(disk) {
  const temp = findDiskTemp(disk);
  return temp ? formatTemp(temp) : "--";
}

function diskHealthText(disk) {
  const health = disk && disk.health ? `${disk.health}` : "";
  if (/healthy/i.test(health)) {
    return "OK";
  }
  return health || "--";
}

function renderStorage() {
  const disk = primaryStorageDisk();
  const volumes = volumesForDisk(disk);
  const usedPercent = Math.round(diskUsedPercent(disk));
  setText(ids.storageUsage, `${usedPercent}%`);
  setText(ids.storageModel, shortDiskModel(disk));
  setText(ids.storageTemp, diskTempText(disk));
  setText(ids.storageFree, freeSummary(volumes, 3));
  setLevel(ids.storageUsage, usedPercent);
}

function renderNetwork() {
  const adapter = bestNetworkAdapter();
  if (!ids.network || !ids.networkName) {
    return;
  }

  if (!adapter || adapter.status === "Missing") {
    setText(ids.networkName, "Network");
    setText(ids.networkLink, "--");
    setText(ids.networkRx, "--");
    setText(ids.networkTx, "--");
    setLevel(ids.network, 0);
    return;
  }

  setText(ids.networkName, adapter.alias || adapter.key || "Network");
  setText(ids.networkLink, adapter.linkSpeedBps > 0 ? bitsTextShort(adapter.linkSpeedBps) : "--");
  setText(ids.networkRx, adapter.rxText || "0B/s");
  setText(ids.networkTx, adapter.txText || "0B/s");
  const rate = (adapter.rxBytesPerSec || 0) + (adapter.txBytesPerSec || 0);
  setLevel(ids.network, Math.min(100, rate / (2 * 1024 * 1024) * 100));
}

function render() {
  const cpuTemp = pick("TCPUPKG", "TCPU", "TCPU1") || pickByRegex(/cpu.*temp|tcpu/i);
  const cpuLoad = pick("SCPUUTI", "SCPUUTI1") || pickByRegex(/cpu.*util|cpu.*load/i);
  const cpuClock = pick("SCPUCLK") || pickByRegex(/cpu.*clock/i);
  const cpuPower = pick("PCPUPKG", "PCPU") || pickByRegex(/cpu.*package|cpu.*power/i);
  const gpuTemp = pick("TGPU1", "TGPU") || pickByRegex(/gpu.*temp/i);
  const gpuLoad = pick("SGPU1UTI", "SGPUUTI") || pickByRegex(/gpu.*util|gpu.*load/i);
  const gpuClock = pick("SGPU1CLK") || pickByRegex(/gpu.*clock|sgpu.*clk/i);
  const gpuPower = pickByRegex(/gpu.*power|pgpu/i);
  const caseFan = pickFanByRegex(/chassis|case/i);
  const pump = pick("FPUMP1", "FPUMP") || pickByRegex(/pump/i);
  const memory = pick("SMEMUTI", "SUSEDMEM") || pickByRegex(/memory.*util|mem.*util|ram/i);
  const memoryUsed = pick("SUSEDMEM") || pickByRegex(/used.*mem|memory.*used/i);
  const memoryFree = pick("SFREEMEM") || pickByRegex(/free.*mem|memory.*free/i);
  const memorySpeed = pick("SMEMSPEED") || pickByRegex(/memory.*speed|mem.*speed/i);

  setUnitValue(ids.cpuTemp, tempNumber(cpuTemp), CELSIUS);
  setText(ids.cpuLoad, formatPercent(cpuLoad));
  setText(ids.cpuClock, formatCpuClock(cpuClock));
  setText(ids.cpuPower, formatPower(cpuPower));
  setUnitValue(ids.gpuTemp, tempNumber(gpuTemp), CELSIUS);
  setText(ids.gpuLoad, formatPercent(gpuLoad));
  setText(ids.gpuClockHome, formatClock(gpuClock));
  setText(ids.gpuPowerHome, formatPower(gpuPower));
  setText(ids.caseFan, formatRpm(caseFan));
  setText(ids.pumpHome, formatRpm(pump));
  setText(ids.memory, formatPercent(memory));
  setText(ids.memoryUsed, formatMemoryMb(memoryUsed));
  const memoryTotalMb = (number(memoryUsed) || 0) + (number(memoryFree) || 0);
  setText(ids.memoryTotal, memoryTotalMb > 0 ? formatMb(memoryTotalMb) : "--");
  setText(ids.memorySpeed, sensorText(memorySpeed));
  setLevel(ids.cpuLoad, number(cpuLoad));
  setLevel(ids.cpuClock, Math.min(100, (number(cpuClock) || 0) / 6000 * 100));
  setLevel(ids.cpuPower, Math.min(100, (number(cpuPower) || 0) / 250 * 100));
  setLevel(ids.cpuTemp, Math.min(100, (number(cpuTemp) || 0) / 90 * 100));
  setLevel(ids.gpuTemp, Math.min(100, (number(gpuTemp) || 0) / 90 * 100));
  setLevel(ids.gpuLoad, number(gpuLoad));
  setLevel(ids.gpuPowerHome, Math.min(100, (number(gpuPower) || 0) / 450 * 100));
  setLevel(ids.caseFan, Math.min(100, (number(caseFan) || 0) / 1800 * 100));
  setLevel(ids.memory, number(memory));

  const cpuTone = tempTone(number(cpuTemp));
  const gpuTone = tempTone(number(gpuTemp));
  setTone(cards.cpu, cpuTone);
  setTone(cards.gpu, gpuTone);
  const toneColors = currentToneColors();
  if (sparks.cpuTemp) sparks.cpuTemp.setColor(toneColors[cpuTone]);
  if (sparks.gpuTemp) sparks.gpuTemp.setColor(toneColors[gpuTone]);

  const count = snapshot && snapshot.sensors ? Object.keys(snapshot.sensors).length : 0;
  setText(ids.sensorCount, `${count} sensors`);
  setText(ids.updated, snapshot ? timeText(snapshot.updatedAt) : "--");
  renderNetwork();
  renderStorage();
  renderDetail();
}

function renderDetail() {
  if (currentPage === "home" || !ids.detailView || ids.detailView.hidden) {
    return;
  }

  const detail = makeDetail(currentPage);
  ids.detailSubtitle.textContent = detail.subtitle;
  ids.detailPrimary.className = textClass(detail.primary, 8, 13);
  if (detail.primaryUnit) {
    setUnitValue(ids.detailPrimary, detail.primary, detail.primaryUnit);
  }
  else {
    setText(ids.detailPrimary, detail.primary);
  }
  ids.detailPrimaryLabel.textContent = detail.primaryLabel;
  ids.detailFooterLeft.textContent = detail.footer;
  ids.detailUpdated.textContent = snapshot ? timeText(snapshot.updatedAt) : "--";
  if (detail.layout === "storage") {
    setMetricCards([]);
    setStorageDiskCards(detail.disks);
  }
  else if (detail.layout === "network") {
    setMetricCards([]);
    setNetworkAdapterCards(detail.adapters);
  }
  else {
    setMetricCards(detail.metrics);
    setRows(detail.rows);
  }
}

function makeDetail(page) {
  switch (page) {
    case "cpu":
      return cpuDetail();
    case "gpu":
      return gpuDetail();
    case "power":
      return powerDetail();
    case "network":
      return networkDetail();
    case "memory":
      return memoryDetail();
    case "cooling":
      return coolingDetail();
    case "board":
      return boardDetail();
    default:
      return emptyDetail();
  }
}

function cpuDetail() {
  const temp = pick("TCPUPKG", "TCPU", "TCPU1") || pickByRegex(/cpu.*temp|tcpu/i);
  const load = pick("SCPUUTI", "SCPUUTI1") || pickByRegex(/cpu.*util|cpu.*load/i);
  const clock = pick("SCPUCLK") || pickByRegex(/cpu.*clock/i);
  const power = pick("PCPUPKG", "PCPU") || pickByRegex(/cpu.*package|cpu.*power/i);
  const vid = pickByRegex(/cpu.*vid|vcpu/i);
  const exclude = collectIds(temp, load, clock, power, vid);
  const rows = rowsByRegex(/cpu|core|package/i, 8, null, exclude);
  return {
    subtitle: "Processor",
    primary: tempNumber(temp),
    primaryUnit: CELSIUS,
    primaryLabel: pages.cpu.primaryLabel,
    footer: `${rows.length} more`,
    metrics: [
      ["Load", formatPercent(load)],
      ["Clock", formatCpuClock(clock)],
      ["Package", formatPower(power)],
      ["VID", formatVolt(vid)]
    ],
    rows
  };
}

function gpuDetail() {
  const temp = pick("TGPU1", "TGPU") || pickByRegex(/gpu.*temp/i);
  const load = pick("SGPU1UTI", "SGPUUTI") || pickByRegex(/gpu.*util|gpu.*load/i);
  const clock = pickByRegex(/gpu.*clock|sgpu.*clk/i);
  const power = pickByRegex(/gpu.*power|pgpu/i);
  const fan = pickFanByRegex(/gpu.*fan|fan.*gpu|^fgpu/i);
  const exclude = collectIds(temp, load, clock, power, fan);
  const rows = rowsByRegex(/gpu|vram/i, 8, null, exclude);
  return {
    subtitle: "Graphics",
    primary: tempNumber(temp),
    primaryUnit: CELSIUS,
    primaryLabel: pages.gpu.primaryLabel,
    footer: `${rows.length} more`,
    metrics: [
      ["Load", formatPercent(load)],
      ["Clock", formatClock(clock)],
      ["Power", formatPower(power)],
      ["Fan", formatRpm(fan)]
    ],
    rows
  };
}

function powerDetail() {
  const cpuPower = pick("PCPUPKG", "PCPU") || pickByRegex(/cpu.*package|cpu.*power/i);
  const gpuPower = pickByRegex(/gpu.*power|pgpu/i);
  const cpuVid = pickByRegex(/cpu.*vid|vcpu/i);
  const gpuVolt = pickByRegex(/gpu.*volt|vgpu/i);
  const exclude = collectIds(cpuPower, gpuPower, cpuVid, gpuVolt);
  const rows = allSensors()
    .filter(sensor => {
      if (exclude.has(sensor.id)) return false;
      const kind = sensor.kind || "";
      const label = sensor.label || "";
      return /pwr|power/i.test(kind) || /power|package|watt/i.test(label);
    })
    .slice(0, 8)
    .map(sensor => ({ label: sensor.label || sensor.id, value: formatSensor(sensor) }));
  return {
    subtitle: "Package",
    primary: formatPower(cpuPower),
    primaryLabel: pages.power.primaryLabel,
    footer: `${rows.length} more`,
    metrics: [
      ["CPU", formatPower(cpuPower)],
      ["GPU", formatPower(gpuPower)],
      ["CPU VID", formatVolt(cpuVid)],
      ["GPU Volt", formatVolt(gpuVolt)]
    ],
    rows
  };
}

function networkDetail() {
  const adapters = networkSnapshot && networkSnapshot.adapters
    ? Object.values(networkSnapshot.adapters)
    : [];
  const rateOf = a => (a.rxBytesPerSec || 0) + (a.txBytesPerSec || 0);
  const sorted = adapters.slice().sort((a, b) => {
    const upDiff = (b.status === "Up" ? 1 : 0) - (a.status === "Up" ? 1 : 0);
    if (upDiff !== 0) return upDiff;
    const ra = rateOf(a);
    const rb = rateOf(b);
    if (ra !== rb) return rb - ra;
    return (a.alias || "").localeCompare(b.alias || "");
  });
  return {
    layout: "network",
    subtitle: "Adapters",
    primary: "--",
    primaryLabel: "--",
    footer: `${sorted.length} adapters`,
    metrics: [],
    rows: [],
    adapters: sorted
  };
}

function memoryDetail() {
  const util = pick("SMEMUTI") || pickByRegex(/memory.*util|mem.*util|ram.*util/i);
  const used = pick("SUSEDMEM") || pickByRegex(/used.*mem|memory.*used/i);
  const free = pick("SFREEMEM") || pickByRegex(/free.*mem|memory.*free/i);
  const speed = pick("SMEMSPEED") || pickByRegex(/memory.*speed|mem.*speed/i);
  const vram = pickByRegex(/vram|gpu.*memory/i);
  const totalMb = (number(used) || 0) + (number(free) || 0);
  const exclude = collectIds(util, used, free, speed, vram);
  const rows = rowsByRegex(/mem|memory|ram|vram/i, 8, null, exclude);
  return {
    subtitle: "RAM",
    primary: formatPercent(util),
    primaryLabel: pages.memory.primaryLabel,
    footer: `${rows.length} more`,
    metrics: [
      ["Used", formatMemoryMb(used)],
      ["Total", formatMb(totalMb)],
      ["Speed", sensorText(speed)],
      ["VRAM", formatMemoryMb(vram)]
    ],
    rows
  };
}

function coolingDetail() {
  const pump = pick("FPUMP1", "FPUMP") || pickByRegex(/pump/i);
  const caseFan = pickFanByRegex(/chassis|case/i);
  const coolant = pickByRegex(/coolant|water|liquid/i);
  const ssdTemp = diskTempSensors(1)[0];
  const vrm = pickByRegex(/vrm|mosfet/i, "temp");
  const exclude = collectIds(pump, caseFan, coolant, ssdTemp, vrm);
  const rows = allSensors()
    .filter(sensor => {
      if (exclude.has(sensor.id)) return false;
      const kind = sensor.kind || "";
      const id = sensor.id || "";
      const label = sensor.label || "";
      const text = `${id} ${label}`;
      return /fan|cooler/i.test(kind)
        || /fan|pump|coolant|water|liquid/i.test(text)
        || (kind === "temp" && /vrm|pch|chipset|board|dimm|ssd|hdd|nvme|m\.2|kingston|wds|zhitai/i.test(text));
    })
    .slice(0, 8)
    .map(sensor => ({ label: sensor.label || sensor.id, value: formatSensor(sensor) }));
  return {
    subtitle: "Liquid Loop",
    primary: formatRpm(caseFan),
    primaryLabel: "Radiator Fan",
    footer: `${rows.length} more`,
    metrics: [
      ["Pump", formatRpm(pump)],
      ["Water", formatTemp(coolant)],
      ["VRM", formatTemp(vrm)],
      ["SSD", formatTemp(ssdTemp)]
    ],
    rows
  };
}

function boardDetail() {
  const disk = primaryStorageDisk();
  const volumes = volumesForDisk(disk);
  const disks = storageDisks();

  return {
    layout: "storage",
    subtitle: "Drives",
    primary: disk ? `${Math.round(diskUsedPercent(disk))}%` : "--",
    primaryLabel: pages.board.primaryLabel,
    footer: `${disks.length} disks`,
    metrics: [
      ["Disk", shortDiskModel(disk)],
      ["Vols", volumeLetters(volumes)],
      ["Temp", diskTempText(disk)],
      ["Health", diskHealthText(disk)]
    ],
    rows: [],
    disks
  };
}

function emptyDetail() {
  return {
    subtitle: "--",
    primary: "--",
    primaryLabel: "--",
    footer: "--",
    metrics: [],
    rows: []
  };
}

function setMetricCards(metrics) {
  ids.detailMetrics.replaceChildren(...metrics.map(([label, value]) => {
    const card = document.createElement("article");
    card.className = "metricCard";
    const span = document.createElement("span");
    span.textContent = label;
    const strong = document.createElement("strong");
    strong.textContent = value;
    const len = `${value || ""}`.length;
    strong.className = len >= 16 ? "veryLongText" : len >= 11 ? "longText" : "";
    card.append(span, strong);
    return card;
  }));
}

function textClass(text, mediumAt, longAt) {
  const length = `${text || ""}`.length;
  if (length >= longAt) {
    return "longText";
  }
  if (length >= mediumAt) {
    return "mediumText";
  }
  return "";
}

function setRows(rows) {
  ids.detailList.className = "detailList";
  ids.detailList.replaceChildren(...rows.map(row => {
    const item = document.createElement("div");
    item.className = "detailRow";
    const label = document.createElement("span");
    label.textContent = row.label;
    const value = document.createElement("b");
    value.textContent = row.value;
    item.append(label, value);
    return item;
  }));
}

function setStorageDiskCards(disks) {
  ids.detailList.className = "detailList storageDiskList";
  if (!disks || disks.length === 0) {
    const empty = document.createElement("article");
    empty.className = "storageDiskCard";
    const title = document.createElement("strong");
    title.textContent = "No fixed drives";
    empty.append(title);
    ids.detailList.replaceChildren(empty);
    return;
  }

  ids.detailList.replaceChildren(...disks.map(disk => {
    const volumes = volumesForDisk(disk);
    const card = document.createElement("article");
    card.className = "storageDiskCard";

    const used = Math.round(diskUsedPercent(disk));
    card.style.setProperty("--level", `${used}%`);
    card.dataset.tone = used >= 95 ? "hot" : used >= 85 ? "warn" : "good";

    const head = document.createElement("div");
    head.className = "storageDiskHead";
    const title = document.createElement("strong");
    title.textContent = shortDiskModel(disk);
    const badge = document.createElement("b");
    badge.textContent = diskHealthText(disk);
    badge.className = /ok|healthy/i.test(badge.textContent) ? "healthOk" : "healthWarn";
    head.append(title, badge);

    const meta = document.createElement("div");
    meta.className = "storageDiskMeta";
    meta.textContent = `${volumeLetters(volumes)} · ${disk.busType || "Disk"} · ${shortBytes(disk.sizeText)}`;

    const stats = document.createElement("div");
    stats.className = "storageDiskStats";
    for (const [label, value] of [
      ["Free", freeSummary(volumes, 3)],
      ["Used", `${used}%`],
      ["Temp", diskTempText(disk)]
    ]) {
      const item = document.createElement("span");
      item.textContent = label;
      const strong = document.createElement("b");
      strong.textContent = value;
      item.append(strong);
      stats.append(item);
    }

    card.append(head, meta, stats);
    return card;
  }));
}

function setNetworkAdapterCards(adapters) {
  ids.detailList.className = "detailList storageDiskList networkAdapterList";

  if (!adapters || adapters.length === 0) {
    const empty = document.createElement("article");
    empty.className = "storageDiskCard";
    const title = document.createElement("strong");
    title.textContent = "No adapters";
    empty.append(title);
    ids.detailList.replaceChildren(empty);
    return;
  }

  ids.detailList.replaceChildren(...adapters.map(adapter => {
    const card = document.createElement("article");
    card.className = "storageDiskCard networkAdapterCard";
    card.dataset.status = adapter.status || "Missing";

    const head = document.createElement("div");
    head.className = "storageDiskHead";
    const title = document.createElement("strong");
    title.textContent = adapter.alias || adapter.key || "--";
    const badge = document.createElement("b");
    const isUp = adapter.status === "Up";
    badge.textContent = adapter.status || "--";
    badge.className = isUp ? "healthOk" : "healthWarn";
    head.append(title, badge);

    const meta = document.createElement("div");
    meta.className = "storageDiskMeta";
    const ip = adapter.ipAddress && adapter.ipAddress.length > 0 ? adapter.ipAddress : "--";
    const link = adapter.linkSpeedBps > 0 ? bitsText(adapter.linkSpeedBps) : "--";
    meta.textContent = `${ip} · ${link}`;

    const stats = document.createElement("div");
    stats.className = "storageDiskStats";
    for (const [label, value] of [
      ["↓ RX", adapter.rxText || "0B/s"],
      ["↑ TX", adapter.txText || "0B/s"]
    ]) {
      const item = document.createElement("span");
      item.textContent = label;
      const strong = document.createElement("b");
      strong.textContent = value;
      item.append(strong);
      stats.append(item);
    }

    card.append(head, meta, stats);
    return card;
  }));
}

function bitsText(bitsPerSec) {
  const units = ["bps", "Kbps", "Mbps", "Gbps"];
  let value = Math.max(0, Number(bitsPerSec) || 0);
  let unit = 0;
  while (value >= 1000 && unit < units.length - 1) {
    value /= 1000;
    unit++;
  }
  return unit === 0 ? `${value.toFixed(0)}${units[unit]}` : `${value.toFixed(1)}${units[unit]}`;
}

function bitsTextShort(bitsPerSec) {
  const value = Math.max(0, Number(bitsPerSec) || 0);
  if (value >= 1000 * 1000 * 1000) {
    return `${numericText(value / (1000 * 1000 * 1000), 1)}G`;
  }
  if (value >= 1000 * 1000) {
    return `${Math.round(value / (1000 * 1000))}M`;
  }
  if (value >= 1000) {
    return `${Math.round(value / 1000)}K`;
  }
  return `${Math.round(value)}`;
}

function timeText(ms) {
  return new Date(ms).toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit",
    second: "2-digit"
  });
}

render();

const THEME_KEY = "panelTheme";
const THEMES = ["auto", "light", "dark"];
const THEME_ICON = {
  auto: "A",
  light: "☀️",
  dark: "🌙"
};
let themeMode = "auto";
try {
  const stored = localStorage.getItem(THEME_KEY);
  if (THEMES.includes(stored)) themeMode = stored;
} catch (err) {
  // localStorage unavailable, fall back to default
}

function resolveTheme(mode) {
  if (mode === "auto") {
    const hour = new Date().getHours();
    return hour >= 6 && hour < 18 ? "light" : "dark";
  }
  return mode;
}

function applyTheme() {
  const actual = resolveTheme(themeMode);
  if (document.body.dataset.theme !== actual) {
    document.body.dataset.theme = actual;
  }
  document.querySelectorAll(".themeToggle").forEach(btn => {
    btn.textContent = THEME_ICON[themeMode];
    btn.dataset.mode = themeMode;
  });
  if (snapshot) render();
}

document.querySelectorAll(".themeToggle").forEach(btn => {
  btn.addEventListener("pointerup", event => {
    event.preventDefault();
    event.stopPropagation();
    notifyActivity();
    const idx = THEMES.indexOf(themeMode);
    themeMode = THEMES[(idx + 1) % THEMES.length];
    try {
      localStorage.setItem(THEME_KEY, themeMode);
    } catch (err) {
      // ignore
    }
    applyTheme();
    setHomePage(0);   // show home page 1 on load
  });
});

applyTheme();

setInterval(() => {
  if (themeMode === "auto") applyTheme();
}, 60 * 1000);
