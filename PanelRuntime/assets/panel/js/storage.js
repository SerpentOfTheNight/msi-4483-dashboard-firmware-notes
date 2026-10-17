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

function storageVolumes() {
  return storageSnapshot && Array.isArray(storageSnapshot.volumes)
    ? storageSnapshot.volumes
    : [];
}

function storageDisks() {
  const raw = storageSnapshot && Array.isArray(storageSnapshot.disks)
    ? storageSnapshot.disks.slice()
    : [];

  return raw.sort((a, b) => {
    const la = primaryLetter(a);
    const lb = primaryLetter(b);
    if (la && lb && la !== lb) return la.localeCompare(lb);
    if (la && !lb) return -1;
    if (!la && lb) return 1;
    return (Number(a.diskNumber) || 0) - (Number(b.diskNumber) || 0);
  });
}

function primaryLetter(disk) {
  const vols = volumesForDisk(disk)
    .map(v => (v.name || "").replace(":", "").toUpperCase())
    .filter(Boolean)
    .sort();
  return vols[0] || "";
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
  const disks = storageDisks();
  const disk = selectedStorageDisk();
  const volumes = volumesForDisk(disk);
  const usedPercent = Math.round(diskUsedPercent(disk));

  setText(ids.storageUsage, disk ? `${usedPercent}%` : "--");
  setText(ids.storageModel, disk ? shortDiskModel(disk) : "--");
  setText(ids.storageTemp, diskTempText(disk));
  setText(ids.storageFree, freeSummary(volumes, 3));
  setLevel(ids.storageUsage, usedPercent);

  const counter = document.getElementById("storageDiskCounter");
  if (counter) {
    counter.textContent = disks.length
      ? `${storageDiskIndex + 1}/${disks.length}`
      : "";
  }
}

function restartStorageCycle() {
  if (storageCycleTimer) {
    clearInterval(storageCycleTimer);
    storageCycleTimer = null;
  }
  const disks = storageDisks();
  if (disks.length <= 1) return;

  storageCycleTimer = setInterval(() => {
    const list = storageDisks();
    if (list.length <= 1) return;
    storageDiskIndex = (storageDiskIndex + 1) % list.length;
    renderStorage();
    // one frame when the strip changes (cheap)
    if (currentPage === "home") notifyRendered();
  }, STORAGE_CYCLE_MS);
}