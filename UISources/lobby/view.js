(() => {
  const { art, characters } = window.AnimeCSCatalog;
  const { select, selectAll, element, picture, background, read, write } =
    window.AnimeCSUI;
  const lobby = select(".lobby");
  const overlay = select(".overlay");
  const playable = characters.filter((person) =>
    ["Nora", "Michele", "Flor"].includes(person.key),
  );
  const guns = {
    Ember47: ["炽焰-47", "skin.ember47.case"],
    Fold44: ["折线-44", "skin.fold44.showcase"],
  };
  const key = "animecs-lobby-preview";
  const saved = read(key);
  let current = {
    person: playable.some((person) => person.key === saved?.person)
      ? saved.person
      : "Nora",
    gun: Object.hasOwn(guns, saved?.gun) ? saved.gun : "Ember47",
  };
  let draft;
  let opener;

  function renderLobby() {
    const person = playable.find((entry) => entry.key === current.person);
    background("--avatar", person.key + "Icon");
    const figure = element("div", "unit self");
    const sprite = picture(
      art[person.key + "-" + current.gun],
      person.name + "手持" + guns[current.gun][0],
    );
    sprite.className = "sprite";
    const nameplate = element("span", "nameplate");
    nameplate.append(element("em", "", "自己"), element("b", "", "晴野"));
    figure.append(sprite, nameplate);
    select(".figures").replaceChildren(figure);
  }

  function renderDraft() {
    const sprite = select(".fitting img");
    const person = playable.find((entry) => entry.key === draft.person);
    sprite.src = art[draft.person + "-" + draft.gun];
    sprite.alt = person.name + "手持" + guns[draft.gun][0];
    for (const field of ["person", "gun"]) {
      selectAll("[data-" + field + "]").forEach((button) =>
        button.setAttribute(
          "aria-pressed",
          String(button.dataset[field] === draft[field]),
        ),
      );
    }
  }

  function closeConfig() {
    overlay.hidden = true;
    for (const child of lobby.children) child.inert = false;
    if (opener) opener.focus();
  }

  function openConfig(button) {
    opener = button;
    draft = { ...current };
    renderDraft();
    for (const child of lobby.children)
      if (child !== overlay) child.inert = true;
    overlay.hidden = false;
    select(".x").focus();
  }

  function choice(field, id, name, imageKey) {
    const button = element("button", "", name);
    button.dataset[field] = id;
    button.prepend(picture(art[imageKey]));
    button.addEventListener("click", () => {
      draft[field] = id;
      renderDraft();
    });
    return button;
  }

  background("--map-pic", "station");
  for (const person of playable) {
    select(".people").append(
      choice("person", person.key, person.name, person.key + "Icon"),
    );
  }
  for (const [id, [name, image]] of Object.entries(guns)) {
    const item = window.AnimeCSCatalog.items.find(
      (entry) => entry.id === image,
    );
    select(".guns").append(choice("gun", id, name, item.image));
  }

  selectAll('[data-open="config"]').forEach((button) =>
    button.addEventListener("click", () => openConfig(button)),
  );
  selectAll("[data-dismiss]").forEach((button) =>
    button.addEventListener("click", closeConfig),
  );
  select("[data-apply-fit]").addEventListener("click", () => {
    current = { ...draft };
    write(key, current);
    renderLobby();
    select("[data-message]").textContent = "大厅展示已更新。";
    closeConfig();
  });
  overlay.addEventListener("click", (event) => {
    if (event.target === overlay) closeConfig();
  });
  overlay.addEventListener("keydown", (event) => {
    if (event.key === "Escape") closeConfig();
    if (event.key === "Tab") {
      const choices = Array.from(overlay.querySelectorAll("button"));
      const first = choices[0];
      const last = choices.at(-1);
      if (event.shiftKey && document.activeElement === first) {
        event.preventDefault();
        last.focus();
      } else if (!event.shiftKey && document.activeElement === last) {
        event.preventDefault();
        first.focus();
      }
    }
  });
  selectAll("[data-fold]").forEach((button) =>
    button.addEventListener("click", () => {
      const body = select(".note-body");
      body.hidden = !body.hidden;
      selectAll("[data-fold]").forEach((control) =>
        control.setAttribute("aria-expanded", String(!body.hidden)),
      );
    }),
  );
  select("[data-feed]").addEventListener("click", () => {
    const feed = select(".feed");
    feed.hidden = !feed.hidden;
    select("[data-feed]").setAttribute("aria-expanded", String(!feed.hidden));
  });
  renderLobby();
})();
