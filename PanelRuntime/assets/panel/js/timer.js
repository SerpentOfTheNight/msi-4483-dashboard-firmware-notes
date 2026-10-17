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