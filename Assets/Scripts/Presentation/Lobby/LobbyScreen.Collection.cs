using System;
using System.Linq;
using AnimeCS.Content;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        CharacterDefinition _character;
        string _storeCategory="draw";
        static int InventoryOrder(ContentKind kind) => kind switch
        {
            ContentKind.Weapon=>0, ContentKind.Skin=>1, ContentKind.Character=>2,
            ContentKind.Outfit=>3, ContentKind.Wearable=>4, _=>5
        };

        static string KindName(ContentKind kind) => kind switch
        {
            ContentKind.Weapon=>"武器", ContentKind.Skin=>"枪械涂装", ContentKind.Character=>"角色",
            ContentKind.Outfit=>"角色服装", ContentKind.Wearable=>"服饰配件", ContentKind.Utility=>"战术道具", _=>"物品"
        };

        static string ItemType(ContentDefinition item) => item is WeaponDefinition w ? w.Class switch
        {
            WeaponClass.Pistol=>"手枪", WeaponClass.Smg=>"冲锋枪", WeaponClass.Rifle=>"步枪",
            WeaponClass.LightSniper=>"轻型狙击枪", WeaponClass.HeavySniper=>"重型狙击枪", _=>"近战刀具"
        } : KindName(item.Kind);

        static string SkinTitle(WeaponSkinDefinition skin)
        {
            string prefix = skin.Weapon ? skin.Weapon.DisplayName + " · " : "";
            return prefix.Length > 0 && skin.DisplayName.StartsWith(prefix, StringComparison.Ordinal)
                ? skin.DisplayName.Substring(prefix.Length) : skin.DisplayName;
        }

        static Texture2D Art(ContentDefinition item)
        {
            if (!item) return null;
            if (item.Thumbnail) return item.Thumbnail;
            return item switch
            {
                CharacterDefinition c=>c.Icon, OutfitDefinition o=>o.Character?.Icon,
                WeaponSkinDefinition s=>s.Weapon?.Thumbnail, _=>item.Glyph
            };
        }

        void SelectCharacter(CharacterDefinition character)
        {
            if (!character || !Owned(character))
                character=_catalog.Entries.OfType<CharacterDefinition>().FirstOrDefault(Owned);
            _character=character;
            if (!character) return;
            var outfit=_catalog.Entries.OfType<OutfitDefinition>().FirstOrDefault(o=>Owned(o) && o.Character==character);
            if (outfit) _draft.Select(outfit);
            _host.Preview.ShowWardrobe(_draft,_gun,false,unarmed:true,previewWorn:_draft.WornFor(character.Faction));
        }

        void Characters()
        {
            var rail=_screen.Q<ScrollView>("character-rail"); rail.Clear();
            var owned=_catalog.Entries.OfType<CharacterDefinition>().Where(Owned).ToArray();
            SectionLabel(rail,"角色背包    "+owned.Length+" 名");
            var grid=new VisualElement();grid.AddToClassList("choice-row");rail.Add(grid);
            foreach (var entry in owned.OrderBy(c => c.Faction))
            {
                var card = new Button(() => { SelectCharacter(entry); Render(); }) { name = entry.Id };
                card.AddToClassList("roster-card");
                card.AddToClassList(entry.Faction == Side.Attack ? "faction-attack" : "faction-defend");
                card.EnableInClassList("selected", _character == entry);
                card.Add(new Image { image = entry.Icon, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore });
                card.Add(new Label(entry.DisplayName) { pickingMode = PickingMode.Ignore });
                grid.Add(card);
            }
            if (!_character) return;
            _sheet.Q<Label>("stage-name").text=_character.DisplayName;
            var profile = new VisualElement();
            profile.AddToClassList("character-info");
            _content.Add(profile);
            var team = new Label(CharacterDefinition.FactionName(_character.Faction));
            team.AddToClassList("character-faction");
            team.AddToClassList(_character.Faction == Side.Attack ? "faction-attack" : "faction-defend");
            profile.Add(team);
            var heading = new Label(_character.DisplayName);
            heading.AddToClassList("character-title");
            profile.Add(heading);
            profile.Add(new Label("所属阵营   " + (_character.Faction == Side.Attack ? "攻方" : "守方")));
            profile.Add(new Label("好感度      未记录"));

            var archive = new VisualElement();
            archive.AddToClassList("character-info");
            _content.Add(archive);
            SectionLabel(archive, "个人档案");
            var outfit=_catalog.Find<OutfitDefinition>(_draft.outfitId);
            archive.Add(new Label("基础造型   "+(outfit?outfit.DisplayName:_character.DisplayName)));
            if (_character.Portrait)
            {
                SectionLabel(archive, "立绘相册");
                var portrait = new Button(() => Dialog("portrait")) { name = "open-portrait" };
                portrait.AddToClassList("album-card");
                portrait.Add(new Image { image = _character.Portrait, scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore });
                portrait.Add(new Label("角色立绘  ↗") { pickingMode = PickingMode.Ignore });
                archive.Add(portrait);
            }
            _stage.schedule.Execute(FrameEquipment);
        }

        void Shop()
        {
            var tabs=Row();
            Choice(tabs,"角色招募",_storeCategory=="draw",()=>{_storeCategory="draw";Render();},"shop-draw");
            Choice(tabs,"外观商店",_storeCategory=="cosmetics",()=>{_storeCategory="cosmetics";Render();},"shop-cosmetics");
            SectionLabel(_content,$"可用代币   {State.credits:N0}");
            if (_storeCategory=="draw") Recruitment();
            else Cosmetics();
        }

        void Recruitment()
        {
            var pool=(State.drawPool??Array.Empty<string>()).Select(_catalog.Find<CharacterDefinition>).Where(c=>c).ToArray();
            var featured=_catalog.Find<CharacterDefinition>(State.lastDrawId);
            if (!featured) featured=pool.FirstOrDefault(c=>!Owned(c)) ?? pool.FirstOrDefault();
            var banner=new VisualElement();banner.AddToClassList("feature");_content.Add(banner);
            if (featured) banner.Add(new Image{image=featured.Portrait?featured.Portrait:featured.Icon,scaleMode=ScaleMode.ScaleToFit});
            var info=new VisualElement();info.AddToClassList("feature-info");banner.Add(info);
            var tag=new Label("RECRUITMENT  /  角色招募");tag.AddToClassList("eyebrow");info.Add(tag);
            SectionLabel(info,featured?featured.DisplayName:"角色招募");
            if (!string.IsNullOrEmpty(State.lastDrawId)) info.Add(new Label("上次招募   "+DisplayName(State.lastDrawId)));
            var draw=ActionButton(info,$"招募一次    {State.drawCost} 代币","draw-character",()=>RunAction(LobbyAccountAction.Draw,after:()=>
            {
                _profile=_host.ReadWardrobe();
                if (_section=="shop") Render();
                Notice("招募获得    "+DisplayName(State.lastDrawId));
            }),true);
            draw.SetEnabled(State.credits>=State.drawCost && pool.Length>0);
            Header("招募角色");
            var cards=Row();cards.AddToClassList("inventory-grid");
            foreach(var character in pool)
            {
                var card=new VisualElement();card.AddToClassList("item-tile");cards.Add(card);
                card.Add(new Image{image=character.Icon,scaleMode=ScaleMode.ScaleToFit});
                card.Add(new Label(character.DisplayName));
                card.Add(new Label(Owned(character)?"已获得":"待招募"));
            }
        }

        void Cosmetics()
        {
            var offers=State.offers??Array.Empty<AnimeCS.Accounts.ShopOffer>();
            foreach (var grouping in offers.Select(o=>(offer:o,item:_catalog.Find<ContentDefinition>(o.itemId)))
                .Where(x=>x.item && !x.item.Archived && (x.item is OutfitDefinition || x.item is WeaponSkinDefinition || x.item is WearableDefinition))
                .GroupBy(x=>x.item.Kind))
            {
                Header(KindName(grouping.Key));
                var grid=Row();grid.AddToClassList("inventory-grid");
                foreach(var listing in grouping)
                {
                    var item=listing.item;
                    var card=new VisualElement();card.AddToClassList("item-tile");grid.Add(card);
                    card.Add(new Image{image=Art(item),scaleMode=ScaleMode.ScaleToFit});card.Add(new Label(item.DisplayName));
                    bool owned=Owned(item);
                    var buy=ActionButton(card,owned?"已拥有":listing.offer.price+" 代币  ·  购买","buy-"+listing.offer.id,()=>
                        RunAction(LobbyAccountAction.Buy,listing.offer.id,()=>{
                            _profile=_host.ReadWardrobe();if (_section=="shop") Render();Notice(item.DisplayName+" 已加入库存");}));
                    buy.SetEnabled(!owned && State.credits>=listing.offer.price);
                }
            }
            if (offers.Length==0) _content.Add(new Label("暂无上架商品"));
        }
    }
}
