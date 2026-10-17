window.addEventListener("storage-snapshot", (event) => {
  storageSnapshot = event.detail;
  const disks = storageDisks();
  if (storageDiskIndex >= disks.length) storageDiskIndex = 0;
  renderStorage();
  restartStorageCycle();
  renderDetail();
});

// adds support for cycling the shown storge spec on the home screen 
let storageDiskIndex = 0;
let storageCycleTimer = null;
const STORAGE_CYCLE_MS = 5000; // 2–3s
function storageDiskList() {
  return typeof storageDisks === "function" ? storageDisks() : [];
}

function selectedStorageDisk() {
  const disks = storageDisks();
  if (!disks.length) return null;
  storageDiskIndex = ((storageDiskIndex % disks.length) + disks.length) % disks.length;
  return disks[storageDiskIndex];
}



function cycleStorageDisk(delta = 1) {
  const disks = storageDiskList();
  if (disks.length <= 1) return;
  storageDiskIndex = (storageDiskIndex + delta + disks.length) % disks.length;
  renderStorage();
  notifyActivity();
  notifyRendered();
}
