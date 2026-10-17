function fireTimerAlert(kind) {
  // 1) always show this page
  openPage("timer");
  document.body.classList.add("timerAlarming");

  // 2) keep panel responsive / brighter refresh briefly
  notifyActivity();
  postWebMessage("fps:high");
  setTimeout(() => postWebMessage("fps:low"), 15000);

  // 3) sound via host (PC speakers / later device)
  postWebMessage("alarm:play");

  // 4) status
  setText(document.getElementById("timerStatus"), kind + "!");
  // auto-clear flash after N seconds or on any tap
}

timer = { title: "Timer", kicker: "Clock", primaryLabel: "" };

let timerState = {
  mode: "countdown",      // countdown | alarm | stopwatch
  running: false,
  remainingMs: 0,         // countdown
  elapsedMs: 0,           // stopwatch
  alarmAt: null,          // Date or ms for alarm mode
  alarming: false
};

function formatTimerMs(ms) {
  ms = Math.max(0, Math.floor(ms));
  const s = Math.floor(ms / 1000);
  const m = Math.floor(s / 60);
  const h = Math.floor(m / 60);
  const ss = String(s % 60).padStart(2, "0");
  const mm = String(m % 60).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

function renderTimer() {
  const disp = document.getElementById("timerDisplay");
  const sub = document.getElementById("timerSub");
  const status = document.getElementById("timerStatus");
  const startBtn = document.getElementById("timerStartBtn");

  let main = "00:00";
  let subText = timerState.mode;
  let statusText = timerState.alarming ? "Alarm!" : (timerState.running ? "Running" : "Idle");

  if (timerState.mode === "countdown") {
    main = formatTimerMs(timerState.remainingMs);
  } else if (timerState.mode === "stopwatch") {
    main = formatTimerMs(timerState.elapsedMs);
  } else if (timerState.mode === "alarm") {
    main = timerState.alarmAt
      ? new Date(timerState.alarmAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
      : "--:--";
    subText = "Alarm time";
  }

  if (disp) disp.textContent = main;
  if (sub) sub.textContent = subText;
  if (status) status.textContent = statusText;
  if (startBtn) startBtn.textContent = timerState.running ? "Pause" : "Start";

  // home card
  const homeDisp = document.getElementById("timerHomeDisplay");
  const homeState = document.getElementById("timerHomeState");
  const homeBadge = document.getElementById("timerHomeBadge");
  if (homeDisp) homeDisp.textContent = main;
  if (homeState) homeState.textContent = statusText;
  if (homeBadge) homeBadge.textContent = timerState.alarming ? "!" : (timerState.running ? "ON" : "--");

  // only request a frame when visible or home shows the badge change
  if (currentPage === "timer" || timerState.alarming) {
    notifyRendered();
  }
}

setInterval(() => {
  // advance remainingMs / elapsedMs / check alarm…
  if (timerState.running || timerState.alarming) {
    renderTimer();
  }
}, 250);