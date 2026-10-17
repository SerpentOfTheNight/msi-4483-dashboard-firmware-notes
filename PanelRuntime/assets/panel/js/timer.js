let timerState = {
  mode: "countdown",
  running: false,
  remainingMs: 5 * 60 * 1000,
  elapsedMs: 0,
  alarmAt: null,
  alarming: false,
  lastTick: 0
};

function formatTimerMs(ms) {
  ms = Math.max(0, Math.floor(ms || 0));
  const total = Math.floor(ms / 1000);
  const h = Math.floor(total / 3600);
  const m = Math.floor((total % 3600) / 60);
  const s = total % 60;
  const mm = String(m).padStart(2, "0");
  const ss = String(s).padStart(2, "0");
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

function nextTimeTodayOrTomorrow(h, m) {
  const d = new Date();
  d.setSeconds(0, 0);
  d.setHours(h, m, 0, 0);
  if (d.getTime() <= Date.now()) d.setDate(d.getDate() + 1);
  return d.getTime();
}

function updateTimerChrome() {
  const mode = timerState.mode;
  const pad = document.getElementById("timerPad");
  const adj = document.getElementById("timerAdjust");
  const alarmRow = document.getElementById("timerAlarmRow");

  document.querySelectorAll(".timerModeBtn").forEach((btn) => {
    btn.classList.toggle("active", btn.dataset.timerMode === mode);
  });

  document.querySelectorAll(".timerPresetCd").forEach((b) => {
    b.hidden = mode !== "countdown";
  });
  document.querySelectorAll(".timerPresetAl").forEach((b) => {
    b.hidden = mode !== "alarm";
  });

  if (pad) pad.hidden = mode === "stopwatch";
  if (adj) adj.hidden = mode !== "countdown";
  if (alarmRow) alarmRow.hidden = mode !== "alarm";
}

function renderTimer() {
  const disp = document.getElementById("timerDisplay");
  const sub = document.getElementById("timerSub");
  const status = document.getElementById("timerStatus");
  const startBtn = document.getElementById("timerStartBtn");

  let main = "00:00";
  let subText = timerState.mode;
  let statusText = timerState.alarming
    ? "Alarm!"
    : (timerState.running ? "Running" : "Idle");

  if (timerState.mode === "countdown") {
    main = formatTimerMs(timerState.remainingMs); // never AM/PM
  } else if (timerState.mode === "stopwatch") {
    main = formatTimerMs(timerState.elapsedMs);   // never AM/PM
  } else if (timerState.mode === "alarm") {
    if (timerState.alarmAt) {
      const d = new Date(timerState.alarmAt);
      main = d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
      const mins = Math.max(0, Math.round((timerState.alarmAt - Date.now()) / 60000));
      subText = mins >= 60
        ? `in ${Math.floor(mins / 60)}h ${mins % 60}m`
        : `in ${mins}m`;
    } else {
      main = "--:--";
      subText = "Set a time";
    }
  }

  if (disp) disp.textContent = main;
  if (sub) sub.textContent = subText;
  if (status) status.textContent = statusText;
  if (startBtn) startBtn.textContent = timerState.running ? "Pause" : "Start";

  const homeDisp = document.getElementById("timerHomeDisplay");
  const homeState = document.getElementById("timerHomeState");
  const homeBadge = document.getElementById("timerHomeBadge");
  if (homeDisp) homeDisp.textContent = main;
  if (homeState) homeState.textContent = statusText;
  if (homeBadge) {
    homeBadge.textContent = timerState.alarming ? "!" : (timerState.running ? "ON" : "--");
  }

  updateTimerChrome();

  if (currentPage === "timer" || timerState.alarming || timerState.running) {
    notifyRendered();
  }
}

function fireTimerAlert(kind) {
  timerState.running = false;
  timerState.alarming = true;
  openPage("timer");
  document.body.classList.add("timerAlarming");
  notifyActivity();
  postWebMessage("fps:high");
  setTimeout(() => postWebMessage("fps:low"), 15000);
  postWebMessage("alarm:play");
  renderTimer();
}

function clearTimerAlert() {
  if (!timerState.alarming) return;
  timerState.alarming = false;
  document.body.classList.remove("timerAlarming");
  postWebMessage("alarm:stop");
  renderTimer();
}

function timerTick() {
  const now = Date.now();
  if (!timerState.lastTick) timerState.lastTick = now;
  const dt = now - timerState.lastTick;
  timerState.lastTick = now;

  if (timerState.alarming) return;

  if (timerState.running) {
    if (timerState.mode === "countdown") {
      timerState.remainingMs = Math.max(0, timerState.remainingMs - dt);
      if (timerState.remainingMs <= 0) {
        fireTimerAlert("countdown");
        return;
      }
    } else if (timerState.mode === "stopwatch") {
      timerState.elapsedMs += dt;
    } else if (timerState.mode === "alarm" && timerState.alarmAt && now >= timerState.alarmAt) {
      fireTimerAlert("alarm");
      return;
    }
  }

  if (timerState.running || currentPage === "timer") renderTimer();
}

function setTimerMode(mode) {
  clearTimerAlert();
  timerState.mode = mode;
  timerState.running = false;
  timerState.lastTick = 0;
  if (mode === "countdown" && timerState.remainingMs <= 0) {
    timerState.remainingMs = 5 * 60 * 1000;
  }
  if (mode === "stopwatch") timerState.elapsedMs = 0;
  renderTimer();
  notifyActivity();
}

function toggleTimerRun() {
  clearTimerAlert();
  if (timerState.mode === "countdown" && timerState.remainingMs <= 0) return;
  if (timerState.mode === "alarm" && !timerState.alarmAt) {
    timerState.alarmAt = Date.now() + 60 * 1000;
  }
  timerState.running = !timerState.running;
  timerState.lastTick = Date.now();
  renderTimer();
  notifyActivity();
}

function resetTimer() {
  clearTimerAlert();
  timerState.running = false;
  timerState.lastTick = 0;
  if (timerState.mode === "countdown") {
    timerState.remainingMs = 5 * 60 * 1000;
  } else if (timerState.mode === "stopwatch") {
    timerState.elapsedMs = 0;
  } else {
    timerState.alarmAt = null;
  }
  renderTimer();
  notifyActivity();
}

// --- wire once ---
document.querySelectorAll("[data-timer-mode]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    setTimerMode(btn.getAttribute("data-timer-mode"));
  });
});

document.querySelectorAll("[data-timer-preset]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const sec = Number(btn.getAttribute("data-timer-preset")) || 0;
    clearTimerAlert();
    timerState.mode = "countdown";
    timerState.running = false;
    timerState.remainingMs = sec * 1000;
    renderTimer();
    notifyActivity();
  });
});

document.querySelectorAll("[data-alarm-clock]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const [h, m] = (btn.getAttribute("data-alarm-clock") || "7:00").split(":").map(Number);
    clearTimerAlert();
    timerState.mode = "alarm";
    timerState.running = false;
    timerState.alarmAt = nextTimeTodayOrTomorrow(h, m || 0);
    renderTimer();
    notifyActivity();
  });
});

document.querySelectorAll("[data-timer-adj]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const sec = Number(btn.getAttribute("data-timer-adj")) || 0;
    if (timerState.mode === "countdown") {
      timerState.remainingMs = Math.max(0, timerState.remainingMs + sec * 1000);
      renderTimer();
      notifyActivity();
    }
  });
});

document.querySelectorAll("[data-alarm-adj]").forEach((btn) => {
  btn.addEventListener("click", (ev) => {
    ev.preventDefault();
    ev.stopPropagation();
    const minutes = Number(btn.getAttribute("data-alarm-adj")) || 0;
    if (!timerState.alarmAt) timerState.alarmAt = Date.now() + 60 * 1000;
    timerState.alarmAt = Math.max(Date.now() + 30 * 1000, timerState.alarmAt + minutes * 60 * 1000);
    renderTimer();
    notifyActivity();
  });
});

document.getElementById("timerStartBtn")?.addEventListener("click", (ev) => {
  ev.preventDefault();
  ev.stopPropagation();
  toggleTimerRun();
});

document.getElementById("timerResetBtn")?.addEventListener("click", (ev) => {
  ev.preventDefault();
  ev.stopPropagation();
  resetTimer();
});

document.getElementById("timerView")?.addEventListener("pointerdown", () => {
  if (timerState.alarming) clearTimerAlert();
});

setInterval(timerTick, 250);
renderTimer();