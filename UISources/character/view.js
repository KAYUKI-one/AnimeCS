(() => {
  const { characters, art } = window.AnimeCSCatalog;
  const { select, selectAll, element, picture } = window.AnimeCSUI;
  const list = select(".roster-grid");
  const teams = { attack: "余烬", defend: "苍盾" };

  function show(person) {
    selectAll("[data-roster]").forEach((button) =>
      button.setAttribute(
        "aria-pressed",
        String(button.dataset.roster === person.key),
      ),
    );
    const figure = select("[data-character-figure]");
    figure.src = art[person.key + "Model"];
    figure.alt = person.name + "的全身模型预览";
    select("[data-character-stage]").textContent = person.short;
    select("[data-character-name]").textContent = person.short;
    select("[data-character-roman]").textContent = person.roman;
    const team = select("[data-character-team]");
    team.dataset.side = person.side;
    team.textContent = teams[person.side];
    select("[data-character-assignment]").textContent =
      teams[person.side] + (person.side === "attack" ? " · 攻方" : " · 守方");
    select("[data-character-outfit]").textContent = person.outfit;
    const portrait = select("[data-album-thumb]");
    portrait.src = art[person.key + "Portrait"];
    portrait.alt = person.name + "的立绘";
  }

  for (const person of characters) {
    const button = element("button");
    button.dataset.roster = person.key;
    button.dataset.side = person.side;
    button.setAttribute("aria-label", person.name + " · " + teams[person.side]);
    button.append(
      picture(art[person.key + "Icon"]),
      element("strong", "", person.short),
    );
    button.addEventListener("click", () => show(person));
    list.append(button);
  }
  select("[data-roster-count]").textContent = characters.length + " 名角色";
  show(characters[0]);
})();
