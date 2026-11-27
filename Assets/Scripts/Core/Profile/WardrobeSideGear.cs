using System;
using AnimeCS.Content;

namespace AnimeCS.Profile
{
    [Serializable]
    public sealed class WardrobeSideGear
    {
        public string knifeId, pistolId, smgId, rifleId, sniperId;
        public WornItems worn;

        public string Weapon(WeaponClass kind) => kind switch
        {
            WeaponClass.Knife => knifeId,
            WeaponClass.Pistol => pistolId,
            WeaponClass.Smg => smgId,
            WeaponClass.Rifle => rifleId,
            _ => sniperId
        };

        public void SetWeapon(WeaponClass kind, string id)
        {
            switch (kind)
            {
                case WeaponClass.Knife: knifeId = id; break;
                case WeaponClass.Pistol: pistolId = id; break;
                case WeaponClass.Smg: smgId = id; break;
                case WeaponClass.Rifle: rifleId = id; break;
                default: sniperId = id; break;
            }
        }

        internal static WeaponClass Category(WeaponClass kind) =>
            kind == WeaponClass.HeavySniper ? WeaponClass.LightSniper : kind;

        internal static bool Available(WeaponDefinition weapon, Side side) =>
            weapon && (weapon.Class == WeaponClass.Knife || weapon.CanBuy(side));
    }
}
