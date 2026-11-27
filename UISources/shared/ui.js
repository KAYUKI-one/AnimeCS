(() => {
  const root = document.getElementById("acs-sky-party");
  const { art } = window.AnimeCSCatalog;

  function fitDesktop() {
    const width = Math.min(window.innerWidth, (window.innerHeight * 16) / 9);
    root.style.maxWidth = width + "px";
    root.style.setProperty("--lobby-scale", String(root.clientWidth / 1024));
  }

  function background(property, mediaId) {
    root.style.setProperty(property, 'url("' + art[mediaId] + '")');
  }

  function element(tag, className, label) {
    const node = document.createElement(tag);
    if (className) node.className = className;
    if (label) node.textContent = label;
    if (tag === "button") node.type = "button";
    return node;
  }

  function picture(url, alt = "") {
    const image = element("img");
    image.src = url;
    image.alt = alt;
    image.draggable = false;
    return image;
  }

  function visual(item, fallback = "package") {
    const preview = element("span", "item-visual");
    const url = item && art[item.image];
    const image = picture(url || "../assets/icons/" + fallback + ".svg");
    if (!url) image.className = "ico";
    preview.append(image);
    return preview;
  }

  function tile(item, interactive = false) {
    const tile = element(interactive ? "button" : "article", "item-tile");
    const caption = element("span", "item-caption");
    caption.append(
      element("b", "", item.name),
      element("small", "", item.detail),
    );
    tile.dataset.kind = item.kind;
    tile.dataset.item = item.id;
    tile.append(visual(item), caption);
    if (interactive) {
      tile.draggable = true;
      tile.setAttribute("aria-label", "装备 " + item.name + " " + item.detail);
    }
    return tile;
  }

  function read(key) {
    try {
      return JSON.parse(localStorage.getItem(key));
    } catch {
      return null;
    }
  }

  function write(key, value) {
    try {
      localStorage.setItem(key, JSON.stringify(value));
    } catch {
      // The prototype still supports a temporary selection in private browser contexts.
    }
  }

  fitDesktop();
  window.addEventListener("resize", fitDesktop);
  background("--courtyard", "courtyard");
  const savedAvatar = read("animecs-lobby-preview")?.person;
  background(
    "--avatar",
    art[savedAvatar + "Icon"] ? savedAvatar + "Icon" : "NoraIcon",
  );
  window.AnimeCSUI = {
    select: (selector) => root.querySelector(selector),
    selectAll: (selector) => Array.from(root.querySelectorAll(selector)),
    element,
    picture,
    visual,
    tile,
    background,
    read,
    write,
  };
})();
