using System.Collections.Generic;
using AnimeCS.Content;

namespace AnimeCS.Match
{
    public struct UtilityCounts
    {
        public const int Capacity=4, PerType=2;
        private int _smoke,_flash,_echo;
        public int Total=>_smoke+_flash+_echo;
        public int Count(UtilityEffect kind)=>kind switch {UtilityEffect.Smoke=>_smoke,UtilityEffect.Flash=>_flash,UtilityEffect.Echo=>_echo,_=>0};
        public bool CanAdd(UtilityEffect kind)=>Total<Capacity && Count(kind)<PerType;
        internal void Add(UtilityEffect kind){switch(kind){case UtilityEffect.Smoke:_smoke++;break;case UtilityEffect.Flash:_flash++;break;case UtilityEffect.Echo:_echo++;break;}}
    }

    internal sealed class UtilityBelt
    {
        private readonly List<UtilityDefinition> _held=new(UtilityCounts.Capacity), _ordered=new(UtilityCounts.Capacity);
        private UtilityDefinition _selected;
        public UtilityDefinition LastOrder=>_ordered.Count>0?_ordered[_ordered.Count-1]:null;
        public UtilityDefinition Selected=>_selected?_selected:FindNext(-1,1);
        public int Cost{get;private set;}
        public UtilityCounts Counts(bool pendingOnly=false)
        {
            var counts=new UtilityCounts();
            if(!pendingOnly)foreach(var item in _held)counts.Add(item.Effect);
            foreach(var item in _ordered)counts.Add(item.Effect);
            return counts;
        }
        private UtilityDefinition FindNext(int kind,int direction)
        {
            for(int i=kind+direction;i>=0 && i<= (int)UtilityEffect.Echo;i+=direction)
            {
                foreach(var item in _held)if((int)item.Effect==i)return item;
                foreach(var item in _ordered)if((int)item.Effect==i)return item;
            }
            return null;
        }
        public bool Step(int direction,bool wrap)
        {
            var current=Selected;if(!current)return false;
            var next=FindNext((int)current.Effect,direction);
            if(!next && wrap)next=FindNext(direction>0?-1:(int)UtilityEffect.Echo+1,direction);
            if(!next || next==current)return false;
            _selected=next;return true;
        }
        public void Enter(int direction)=>_selected=FindNext(direction>0?-1:(int)UtilityEffect.Echo+1,direction);
        public bool Add(UtilityDefinition item,bool pending)
        {
            if(!item || item.Archived || !Counts().CanAdd(item.Effect))return false;
            if(pending){_ordered.Add(item);Cost+=item.Price;}else _held.Add(item);
            return true;
        }
        public void Cancel(UtilityDefinition item)
        {
            int i=_ordered.LastIndexOf(item);if(i<0)return;
            Cost-=item.Price;_ordered.RemoveAt(i);RepairSelection();
        }
        public UtilityDefinition Take()
        {
            var item=Selected;if(!item || !_held.Remove(item))return null;
            RepairSelection();return item;
        }
        private void RepairSelection(){if(_selected && !_held.Contains(_selected) && !_ordered.Contains(_selected))_selected=null;}
        public void Commit(){_held.AddRange(_ordered);_ordered.Clear();Cost=0;}
        public void ClearOrders(){_ordered.Clear();Cost=0;RepairSelection();}
        public void Clear(){_held.Clear();ClearOrders();_selected=null;}
    }
}
