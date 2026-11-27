(() => {
  const { items } = window.AnimeCSCatalog;
  const { select, element, tile } = window.AnimeCSUI;
  const groups = [
    ["weapon", "武器"],
    ["skin", "枪械涂装"],
    ["wear", "服装与配件"],
  ];
  const container = select("[data-inventory-groups]");

  for (const [kind, name] of groups) {
    const entries = items.filter((item) => item.kind === kind);
    if (!entries.length) continue;
    const section = element("section", "stock-group");
    const heading = element("h3", "", name);
    heading.append(element("span", "", entries.length + " 件"));
    const grid = element("div", "items-grid");
    grid.append(...entries.map((item) => tile(item)));
    section.append(heading, grid);
    container.append(section);
  }
  select("[data-inventory-count]").textContent = "共 " + items.length + " 件";
})();
