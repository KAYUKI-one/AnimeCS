"""Draw the two original stitched insignia used by the demo cast."""
from pathlib import Path
from random import Random
from PIL import Image, ImageDraw

target = Path(__file__).resolve().parents[2] / "Assets/AnimeCS/Art/Wearables/TeamMarks"
target.mkdir(exist_ok=True)
for attack in (True,False):
    size = 384
    img = Image.new("RGB",(size,size), (46,42,38) if attack else (38,53,58))
    draw = ImageDraw.Draw(img)
    rim = (191,114,68) if attack else (94,163,174)
    thread = (242,196,142) if attack else (194,219,216)
    draw.rounded_rectangle((4,4,379,379),radius=28,outline=rim,width=18)
    for x in range(27,365,13):
        draw.line((x,15,x+6,11),fill=thread,width=2)
        draw.line((x,370,x+6,365),fill=thread,width=2)
    for y in range(27,365,13):
        draw.line((10,y+5,15,y),fill=thread,width=2)
        draw.line((368,y+6,373,y),fill=thread,width=2)
    if attack:
        for y in (72,172):
            draw.polygon([(64,y+72),(192,y),(320,y+72),(320,y+109),(192,y+40),(64,y+109)],fill=rim)
            draw.line([(66,y+69),(192,y),(319,y+70)],fill=thread,width=4)
        draw.polygon([(170,297),(192,280),(214,297),(192,316)],fill=thread)
    else:
        draw.polygon([(192,65),(306,106),(292,225),(244,280),(192,317),(140,280),(92,225),(78,106)],fill=rim)
        draw.polygon([(192,92),(279,124),(266,213),(228,257),(192,283),(156,257),(118,213),(105,124)],fill=(40,57,62))
        draw.polygon([(166,190),(191,160),(219,190),(191,221)],fill=thread)
    pixel=img.load()
    random=Random(42)
    for y in range(size):
        for x in range(size):
            warp=(4 if (x//2+y)%5==0 else -2) + (3 if (x-y//2)%8==0 else 0) +random.randrange(-4,5)
            pixel[x,y]=tuple(max(0,min(255,v+warp)) for v in pixel[x,y])
    img.resize((256,256),Image.Resampling.LANCZOS).save(target / ("AttackWeave.png" if attack else "DefendWeave.png"))
