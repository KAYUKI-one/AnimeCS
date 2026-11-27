using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UnityEngine;

namespace AnimeCS.Match
{
    // Both protocol versions use little-endian, fixed offsets and bounded UTF-8 item IDs.
    internal static class SeatWire
    {
        internal const uint Legacy = 0x53434102, Magic = 0x53434103;
        internal const int TimestampBytes = 6, ExtendedStateBytes = 98, MaxInputBytes = InputBytes + 80 + TimestampBytes;
        internal const byte Hello=1, State=2, Input=3, KeepAlive=4, RequestHandling=6;
        // Older readers ignore this bit in the private state and continue sending Input.
        internal const byte OrderedInputCapability = 4, OrderedInput = 32;
        internal const int InputWindow = 64;
        internal const int PeerTimeoutSeconds = 5;
        // Four animation times, melee result and bounded text identifiers, plus one optional audible event.
        internal const int MaxHandlingBytes = ExtendedStateBytes + 280;
        private const int MaxIdBytes = 80;
        internal const int Header=13, StateBytes=52, InputBytes=46;
        internal static void U32(byte[] p,int at,uint n)=>BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(at,4),n);
        internal static uint U32(byte[] p,int at)=>BinaryPrimitives.ReadUInt32LittleEndian(p.AsSpan(at,4));
        internal static void I32(byte[] p,int at,int n)=>U32(p,at,unchecked((uint)n));
        internal static int I32(byte[] p,int at)=>unchecked((int)U32(p,at));
        internal static void I16(byte[] p,int at,int n)=>BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(at,2),(short)n);
        internal static short I16(byte[] p,int at)=>BinaryPrimitives.ReadInt16LittleEndian(p.AsSpan(at,2));
        internal static ulong Token(byte[] p)=>BinaryPrimitives.ReadUInt64LittleEndian(p.AsSpan(5,8));
        internal static byte[] Packet(byte kind,int size,ulong token)
        {
            var p=new byte[size];U32(p,0,Magic);p[4]=kind;
            BinaryPrimitives.WriteUInt64LittleEndian(p.AsSpan(5,8),token);return p;
        }
        internal static bool Known(byte[] packet, int length) =>
            length >= Header && (U32(packet, 0) == Magic || U32(packet, 0) == Legacy);
        internal static void HeaderFor(byte[] packet, byte kind, uint version, ulong token)
        {
            U32(packet, 0, version);
            packet[4] = kind;
            BinaryPrimitives.WriteUInt64LittleEndian(packet.AsSpan(5, 8), token);
        }
        internal static int WriteHandling(byte[] packet, in SeatView view)
        {
            int at = ExtendedStateBytes;
            I32(packet, at, view.EquipTick);
            I32(packet, at+4, view.InspectTick);
            I32(packet, at+8, view.ReloadStart);
            I32(packet, at+12, view.ReloadEnd);
            at += 16;
            packet[at++] = (byte)view.LastMelee;
            WriteId(packet, ref at, view.Held == HeldSlot.Utility ? view.UtilityId : view.WeaponId);
            WriteId(packet, ref at, view.OutfitId);
            if (view.Heard is not SoundClue clue) { packet[at++] = 0; return at; }
            packet[at++] = 1;
            packet[at++] = (byte)clue.Kind;
            packet[at++] = (byte)clue.WeaponClass;
            packet[at++] = (byte)clue.Melee;
            packet[at++] = (byte)((clue.Own ? 1 : 0) | (clue.Suppressed ? 2 : 0) | ((clue.Bearing & 7) << 2));
            I32(packet, at, clue.Tick);
            at += 4;
            WriteId(packet, ref at, clue.ItemId);
            return at;
        }

        internal static bool ReadHandling(byte[] packet, int size, ref SeatView view, in SeatView previous)
        {
            int at = ExtendedStateBytes;
            if (size < at + 20 || size > MaxHandlingBytes) return false;
            view.EquipTick = I32(packet, at);
            view.InspectTick = I32(packet, at+4);
            view.ReloadStart = I32(packet, at+8);
            view.ReloadEnd = I32(packet, at+12);
            at += 16;
            if (packet[at] > (byte)MeleeAction.Rear) return false;
            view.LastMelee = (MeleeAction)packet[at++];
            bool utility = view.Held == HeldSlot.Utility;
            if (!ReadId(packet, size, ref at, utility ? previous.UtilityId : previous.WeaponId, out var id) || at >= size) return false;
            if (utility) view.UtilityId = id; else view.WeaponId = id;
            if (!ReadId(packet, size, ref at, previous.OutfitId, out view.OutfitId) || at >= size) return false;
            int hasSound = packet[at++];
            if (hasSound == 0) return at == size;
            if (hasSound != 1 || at+9 > size || packet[at] > (byte)SoundKind.DeviceBurst ||
                packet[at+1] > (byte)Content.WeaponClass.Knife || packet[at+2] > (byte)MeleeAction.Rear) return false;
            int flags = packet[at+3];
            var clue = new SoundClue { Kind=(SoundKind)packet[at], WeaponClass=(Content.WeaponClass)packet[at+1],
                Melee=(MeleeAction)packet[at+2], Own=(flags&1)!=0, Suppressed=(flags&2)!=0, Bearing=(flags>>2)&7,
                Tick=I32(packet,at+4) };
            clue.Expires = clue.Tick + 3*MatchRules.TickRate;
            at += 8;
            if (!ReadId(packet, size, ref at, previous.Heard?.ItemId, out clue.ItemId) || at != size) return false;
            view.Heard = clue;
            return true;
        }

        private static void WriteId(byte[] packet, ref int at, string id)
        {
            int count = string.IsNullOrEmpty(id) ? 0 : System.Text.Encoding.UTF8.GetByteCount(id);
            if (count > MaxIdBytes) count = 0;
            packet[at++] = (byte)count;
            if (count == 0) return;
            System.Text.Encoding.UTF8.GetBytes(id, 0, id.Length, packet, at);
            at += count;
        }

        private static bool ReadId(byte[] packet, int size, ref int at, string previous, out string id)
        {
            id = null;
            if (at >= size) return false;
            int length = packet[at++];
            if (length > MaxIdBytes || at+length > size) return false;
            if (length == 0) return true;
            // Canonical IDs in the catalog are ASCII. Reuse the last string across consecutive snapshots.
            bool same = previous?.Length == length;
            for (int i = 0; same && i < length; i++) same = previous[i] == packet[at+i] && packet[at+i] < 128;
            id = same ? previous : System.Text.Encoding.UTF8.GetString(packet, at, length);
            at += length;
            return true;
        }

        internal static int Angle(float x)=>Mathf.RoundToInt(Mathf.DeltaAngle(0,x)*100f);
        internal static int Axis(float x)=>Mathf.RoundToInt(Mathf.Clamp(x,-1,1)*32767f);
        internal static uint Flags(SeatIntent i)=>(i.Fire?1u:0)|(i.FirePressed?2u:0)|(i.Jump?4u:0)|(i.Crouch?8u:0)|
            (i.Walk?16u:0)|(i.Interact?32u:0)|(i.Reload?64u:0)|(i.Zoom?128u:0)|(i.DropHeld?256u:0)|
            (i.Inspect?512u:0)|(i.ThrowUtility?1024u:0)|(i.SwitchWeapon?2048u:0)|(i.LowerScope?4096u:0)|(i.DropObjective?8192u:0);
        internal static bool ValidInput(byte[] p, int length, uint version) =>
            length >= InputBytes && p[45] <= 80 && length == InputBytes + p[45] + (version == Magic ? TimestampBytes : 0)
            && p[42] <= (byte)HeldSlot.Utility && p[43] <= (byte)BuySlot.Helmet && p[44] <= 1 && Math.Abs((int)(sbyte)p[41]) <= 1;
        internal static SeatIntent Intent(byte[] p)
        {
            uint f=U32(p,25);
            return new SeatIntent{Fire=(f&1)!=0,FirePressed=(f&2)!=0,Jump=(f&4)!=0,Crouch=(f&8)!=0,Walk=(f&16)!=0,
                Interact=(f&32)!=0,Reload=(f&64)!=0,Zoom=(f&128)!=0,DropHeld=(f&256)!=0,Inspect=(f&512)!=0,
                ThrowUtility=(f&1024)!=0,SwitchWeapon=(f&2048)!=0,LowerScope=(f&4096)!=0,DropObjective=(f&8192)!=0,
                Yaw=I16(p,29)*.01f,Pitch=I16(p,31)*.01f,Move=new Vector2(I16(p,33),I16(p,35))/32767f,
                BrakeTap=new Vector2(I16(p,37),I16(p,39))/32767f,Cycle=(sbyte)p[41],Select=(HeldSlot)p[42],
                Buy=(BuySlot)p[43],CancelPurchase=p[44]!=0,ItemId=p[45]>0?System.Text.Encoding.UTF8.GetString(p,InputBytes,p[45]):null};
        }
    }
}
