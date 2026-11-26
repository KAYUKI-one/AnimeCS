using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace AnimeCS.Play
{
    internal sealed partial class LobbyScreen
    {
        private void GraphicsOptions()
        {
            var graphics=_game.GraphicsOptions;
            if (graphics==null || !graphics.Supported) return;
            Header("画面");
            var presets=Row();
            var current=graphics.Current;
            var scale=new SliderInt("渲染精度",75,200){name="render-scale",value=current.Scale,showInputField=true};
            scale.AddToClassList("video-field");_content.Add(scale);
            var samples=new List<int>();var aaNames=new List<string>();
            for(int n=1;n<=graphics.MaxSamples;n*=2){samples.Add(n);aaNames.Add(n==1?"关闭":n+"× MSAA");}
            var shadows=new List<int>();var shadowNames=new List<string>();
            for(int n=1024;n<=graphics.MaxShadow;n*=2){shadows.Add(n);shadowNames.Add(n+" × "+n);}
            var aa=new DropdownField("边缘抗锯齿",aaNames,Mathf.Max(0,samples.IndexOf(current.Samples))){name="video-aa"};
            var shadow=new DropdownField("主光阴影",shadowNames,Mathf.Max(0,shadows.IndexOf(current.Shadow))){name="video-shadow"};
            aa.AddToClassList("video-field");shadow.AddToClassList("video-field");_content.Add(aa);_content.Add(shadow);
            var bar=Row();var readout=new Label{name="render-dimensions"};readout.AddToClassList("video-readout");bar.Add(readout);
            string Dimensions(int pct) => pct+"%   "+Mathf.FloorToInt(Screen.width*pct*.01f)+" × "+Mathf.FloorToInt(Screen.height*pct*.01f);
            void Readout() {readout.text=Dimensions(scale.value);}
            void Set(int pct,int multi,int tex)
            {
                scale.value=pct;aa.index=samples.IndexOf(Mathf.Min(multi,graphics.MaxSamples));
                shadow.index=shadows.IndexOf(Mathf.Min(tex,graphics.MaxShadow));Readout();
            }
            Choice(presets,"项目默认",false,()=>Set(graphics.Default.Scale,graphics.Default.Samples,graphics.Default.Shadow),"quality-default");
            Choice(presets,"高精度",false,()=>Set(125,4,4096),"quality-high");
            Choice(presets,"超采样",false,()=>Set(150,8,8192),"quality-ultra");
            scale.RegisterValueChangedCallback(_=>Readout());
            Choice(bar,"应用画质",true,()=>{graphics.Apply(new PictureQuality(scale.value,samples[aa.index],shadows[shadow.index]),true);readout.text=Dimensions(graphics.Current.Scale)+"   已应用";},"apply-graphics");
            Readout();
        }
    }
}

