(() => {
  const { characters, items, art } = window.AnimeCSCatalog;
  const { select, selectAll, element, tile, visual, read, write } =
    window.AnimeCSUI;
  const byId = new Map(items.map((item) => [item.id, item]));
  const byCharacter = new Map(characters.map((person) => [person.key, person]));
  const weaponSlots = [
    ["knife", "近战刀具", "swords"],
    ["pistol", "手枪", "crosshair"],
    ["smg", "冲锋枪", "crosshair"],
    ["rifle", "步枪", "crosshair"],
    ["sniper", "狙击枪", "scan-line"],
  ];
  const wearSlots = [
    ["head", "头饰", "sparkles"],
    ["top", "上衣", "shirt"],
    ["gloves", "手套", "hand"],
    ["bottom", "下装", "layers"],
    ["shoes", "鞋子", "footprints"],
  ];
  const slotNames = new Map(
    [...weaponSlots, ...wearSlots].map(([key, name]) => [key, name]),
  );
  const sideNames = { attack: "余烬", defend: "苍盾" };
  const heldArt = {
    "skin.ember47.case": "Ember47",
    "skin.fold44.showcase": "Fold44",
  };
  const defaults = {
    attack: { person: "Nora", rifle: "skin.ember47.case" },
    defend: { person: "Michele", rifle: "skin.fold44.showcase" },
  };
  const storageKey = "animecs-equipment-preview";
  const saved = read(storageKey);
  const profiles = {};
  const bag = select(".bag-grid");
  const equipment = select(".equipment-page");
  let side = "attack";
  let focusedSlot = "rifle";
  let filter = "all";
  let draggedItem = null;

  function fits(item, team, person) {
    return (
      item &&
      (!item.side || item.side === team) &&
      (!item.character || item.character === person)
    );
  }

  for (const [team, preset] of Object.entries(defaults)) {
    const previous = saved?.[team];
    const person =
      byCharacter.get(previous?.person)?.side === team
        ? previous.person
        : preset.person;
    const slots = {};
    for (const [key] of [...weaponSlots, ...wearSlots]) {
      const item = byId.get(previous?.slots?.[key]);
      if (item?.slot === key && fits(item, team, person)) {
        slots[key] = item.id;
      } else if (weaponSlots.some(([slot]) => slot === key)) {
        const initial =
          key === "rifle"
            ? byId.get(preset.rifle)
            : items.find(
                (entry) =>
                  entry.kind === "weapon" &&
                  entry.slot === key &&
                  fits(entry, team, person),
              );
        if (initial) slots[key] = initial.id;
      }
    }
    profiles[team] = { person, slots };
  }

  function currentTeam() {
    return profiles[side];
  }

  function slotLabel(key, item) {
    return item?.name || (key === "head" ? "未佩戴" : "角色原装");
  }

  function slotButton([key, label, icon]) {
    const item = byId.get(currentTeam().slots[key]);
    const button = element("button", "equip-slot");
    button.dataset.slot = key;
    button.setAttribute("aria-pressed", String(focusedSlot === key));
    const details = element("span", "slot-text");
    const name = slotLabel(key, item);
    details.append(element("small", "", label), element("b", "", name));
    if (item?.kind === "skin")
      details.append(element("small", "", item.detail));
    button.append(visual(item, icon), details);
    button.setAttribute("aria-label", label + " · " + name);
    return button;
  }

  function renderSlots() {
    select("[data-weapon-slots]").replaceChildren(
      ...weaponSlots.map(slotButton),
    );
    select("[data-wear-slots]").replaceChildren(...wearSlots.map(slotButton));
    const canClear =
      wearSlots.some(([key]) => key === focusedSlot) &&
      currentTeam().slots[focusedSlot];
    select("[data-clear-wear]").hidden = !canClear;
  }

  function renderStage() {
    const { person, slots } = currentTeam();
    const actor = byCharacter.get(person);
    const actorSelect = select("[data-equip-person]");
    const candidates = characters.filter((entry) => entry.side === side);
    actorSelect.replaceChildren(
      ...candidates.map((entry) => new Option(entry.short, entry.key)),
    );
    actorSelect.value = person;
    const selected = byId.get(slots[focusedSlot]);
    const held = art[person + "-" + heldArt[selected?.id]];
    const figure = select("[data-equipment-figure]");
    figure.src = held || art[person + "Model"];
    figure.alt =
      actor.name + (held ? "手持" + selected.name : "的全身模型预览");
    const name = slotLabel(focusedSlot, selected);
    const description =
      (held ? "持枪展示" : slotNames.get(focusedSlot)) +
      (selected?.kind === "skin" ? " · " + selected.detail : "");
    const details = element("div");
    details.append(element("small", "", description), element("b", "", name));
    select("[data-stage-item]").replaceChildren(
      visual(selected, "focus"),
      details,
    );
  }

  function renderBag() {
    const previousScroll = bag.scrollTop;
    const eligible = items.filter(
      (item) =>
        item.slot &&
        fits(item, side, currentTeam().person) &&
        (filter === "all" || item.slot === focusedSlot),
    );
    bag.replaceChildren(...eligible.map((item) => tile(item, true)));
    bag.scrollTop = previousScroll;
    select("[data-bag-count]").textContent = eligible.length + " 件";
    selectAll("[data-bag-filter]").forEach((button) =>
      button.setAttribute(
        "aria-pressed",
        String(filter === button.dataset.bagFilter),
      ),
    );
  }

  function render() {
    selectAll("[data-choose-side]").forEach((button) =>
      button.setAttribute(
        "aria-pressed",
        String(side === button.dataset.chooseSide),
      ),
    );
    renderSlots();
    renderStage();
    renderBag();
  }

  function announce(text) {
    select("[data-equip-status]").textContent = text;
  }

  function setItem(item, slot) {
    if (
      !item ||
      item.slot !== slot ||
      !fits(item, side, currentTeam().person)
    ) {
      announce("该物品无法放入此槽位");
      return;
    }
    focusedSlot = slot;
    currentTeam().slots[slot] = item.id;
    render();
    announce(item.name + "已放入" + slotNames.get(slot));
  }

  function clearDrag() {
    draggedItem = null;
    selectAll(".drop-ready,.drop-over").forEach((node) =>
      node.classList.remove("drop-ready", "drop-over"),
    );
  }

  equipment.addEventListener("click", (event) => {
    const button = event.target.closest("button");
    if (!button) return;
    if (button.dataset.chooseSide) {
      side = button.dataset.chooseSide;
      clearDrag();
      render();
      announce(sideNames[side] + "的装备");
    } else if (button.dataset.slot) {
      focusedSlot = button.dataset.slot;
      filter = "slot";
      bag.scrollTop = 0;
      render();
      select('[data-slot="' + focusedSlot + '"]').focus();
    } else if (button.dataset.bagFilter) {
      filter = button.dataset.bagFilter;
      bag.scrollTop = 0;
      renderBag();
    } else if (button.dataset.item) {
      const item = byId.get(button.dataset.item);
      setItem(item, item.slot);
      select('.bag-grid [data-item="' + item.id + '"]')?.focus({
        preventScroll: true,
      });
    }
  });

  select("[data-equip-person]").addEventListener("change", (event) => {
    const team = currentTeam();
    team.person = event.target.value;
    for (const [key, id] of Object.entries(team.slots)) {
      if (!fits(byId.get(id), side, team.person)) delete team.slots[key];
    }
    render();
  });
  select("[data-clear-wear]").addEventListener("click", () => {
    delete currentTeam().slots[focusedSlot];
    render();
    select('[data-slot="' + focusedSlot + '"]').focus();
    announce("已恢复角色原装");
  });
  select("[data-apply-loadout]").addEventListener("click", () => {
    write(storageKey, profiles);
    announce(sideNames[side] + "的搭配已应用");
  });

  bag.addEventListener("dragstart", (event) => {
    const button = event.target.closest("button[data-item]");
    if (!button) return;
    draggedItem = byId.get(button.dataset.item);
    event.dataTransfer.effectAllowed = "move";
    event.dataTransfer.setData("text/plain", draggedItem.id);
    select('[data-slot="' + draggedItem.slot + '"]')?.classList.add(
      "drop-ready",
    );
  });
  equipment.addEventListener("dragover", (event) => {
    const slot = event.target.closest("[data-slot]");
    if (!slot || !draggedItem) return;
    event.preventDefault();
    const valid = slot.dataset.slot === draggedItem.slot;
    event.dataTransfer.dropEffect = valid ? "move" : "none";
    slot.classList.toggle("drop-over", valid);
  });
  equipment.addEventListener("dragleave", (event) => {
    const slot = event.target.closest("[data-slot]");
    if (slot && !slot.contains(event.relatedTarget))
      slot.classList.remove("drop-over");
  });
  equipment.addEventListener("drop", (event) => {
    const slot = event.target.closest("[data-slot]");
    if (!slot || !draggedItem) return;
    event.preventDefault();
    const item = draggedItem;
    clearDrag();
    setItem(item, slot.dataset.slot);
  });
  equipment.addEventListener("dragend", clearDrag);
  render();
})();
