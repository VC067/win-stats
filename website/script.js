const modules = {
  cpu: {
    name: "CPU",
    title: "A closer look at your CPU.",
    description: "Usage, per-core activity, frequency, uptime and the busiest processes.",
    dark: "assets/media/cpu-dark.png",
    light: "assets/media/cpu-light.png",
    alt: "Win Stats CPU card. Numbers are samples.",
    height: 1408,
    note: "Process details are collected only while this card is open."
  },
  ram: {
    name: "Memory",
    title: "Know where your memory goes.",
    description: "Memory in use, standby, free, compressed and the page file.",
    dark: "assets/media/ram-dark.png",
    light: "assets/media/ram-light.png",
    alt: "Win Stats memory card. Numbers are samples.",
    height: 1148,
    note: "Process details are collected only while this card is open."
  },
  gpu: {
    name: "GPU",
    title: "See your graphics at work.",
    description: "Graphics activity, video memory, and temperature when the driver reports it.",
    dark: "assets/media/gpu-dark.png",
    light: "assets/media/gpu-light.png",
    alt: "Win Stats GPU card. Numbers are samples.",
    height: 1087,
    note: "Available readings depend on your graphics hardware and driver."
  },
  disk: {
    name: "Disk",
    title: "Keep track of your drives.",
    description: "How busy the drive is, the read and write rate, and how full it is.",
    dark: "assets/media/disk-dark.png",
    light: "assets/media/disk-light.png",
    alt: "Win Stats disk card. Numbers are samples.",
    height: 596,
    note: "Readings come from Windows performance counters and drive information."
  },
  network: {
    name: "Network",
    title: "Watch your connection.",
    description: "Download and upload, the adapter, and the address on this machine.",
    dark: "assets/media/network-dark.png",
    light: "assets/media/network-light.png",
    alt: "Win Stats network card. Numbers are samples.",
    height: 943,
    note: "Public IP lookup is optional and off by default."
  },
  sensors: {
    name: "Sensors",
    title: "Check the available sensors.",
    description: "Available temperature, fan, clock and power readings from your GPU.",
    dark: "assets/media/sensors-dark.png",
    light: "assets/media/sensors-light.png",
    alt: "Win Stats sensors card. Numbers are samples.",
    height: 895,
    note: "Hardware support varies. CPU temperature is unavailable; no sensor driver is installed."
  }
};

const tabs = [...document.querySelectorAll("[data-module]")];
const title = document.getElementById("module-title");
const description = document.getElementById("module-description");
const image = document.getElementById("module-image");
const panel = document.getElementById("panel");
const darkQuery = window.matchMedia("(prefers-color-scheme: dark)");
const note = document.getElementById("module-note");
const enlarge = document.getElementById("enlarge-preview");
const moduleArt = document.getElementById("module-art");
const previewButtons = [...document.querySelectorAll("[data-preview]")];
const dialog = document.getElementById("preview-dialog");
const fullPreview = document.getElementById("full-preview");
const previewTitle = document.getElementById("preview-title");
let current = "cpu";
let dialogKey = "cpu";

function srcFor(module) {
  return darkQuery.matches ? module.dark : module.light;
}

function setDialogImage(key) {
  const module = modules[key];
  if (!module) return;
  dialogKey = key;
  fullPreview.src = srcFor(module);
  fullPreview.alt = module.alt;
  fullPreview.width = 640;
  fullPreview.height = module.height;
  previewTitle.textContent = `${module.name} preview`;
}

function select(key, focus) {
  const module = modules[key];
  if (!module) return;
  current = key;
  tabs.forEach((tab) => {
    const on = tab.dataset.module === key;
    tab.setAttribute("aria-selected", String(on));
    tab.tabIndex = on ? 0 : -1;
    if (on && focus) tab.focus();
    if (on) panel.setAttribute("aria-labelledby", tab.id);
  });
  title.textContent = module.title;
  description.textContent = module.description;
  image.src = srcFor(module);
  image.alt = module.alt;
  image.width = 640;
  image.height = module.height;
  note.textContent = module.note;
  enlarge.dataset.preview = key;
  moduleArt.dataset.preview = key;
  enlarge.setAttribute("aria-label", `Enlarge ${module.name} preview`);
  moduleArt.setAttribute("aria-label", `Enlarge ${module.name} preview`);
}

tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => select(tab.dataset.module, false));
  tab.addEventListener("keydown", (event) => {
    if (!["ArrowRight", "ArrowLeft", "Home", "End"].includes(event.key)) return;
    event.preventDefault();
    const next = event.key === "Home" ? 0 : event.key === "End" ? tabs.length - 1 :
      event.key === "ArrowRight" ? index + 1 : index - 1;
    select(tabs[(next + tabs.length) % tabs.length].dataset.module, true);
  });
});

previewButtons.forEach((button) => {
  button.addEventListener("click", () => {
    setDialogImage(button.dataset.preview);
    dialog.showModal();
  });
});
document.getElementById("close-preview").addEventListener("click", () => dialog.close());
darkQuery.addEventListener("change", () => {
  select(current, false);
  setDialogImage(dialogKey);
});
select(current, false);
setDialogImage(dialogKey);

const demoVideo = document.querySelector(".demo-video");
if (demoVideo && "IntersectionObserver" in window && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
  const demoObserver = new IntersectionObserver((entries) => {
    if (entries.some((entry) => entry.isIntersecting)) {
      demoVideo.play().catch(() => {});
      demoObserver.disconnect();
    }
  }, { threshold: 0.35 });
  demoObserver.observe(demoVideo);
}

if (window.gsap && !window.matchMedia("(prefers-reduced-motion: reduce)").matches) {
  window.gsap.from(".hero-copy, .overview", { opacity: 0, y: 10, duration: 0.55, stagger: 0.08, ease: "power2.out", clearProps: "all" });
}
