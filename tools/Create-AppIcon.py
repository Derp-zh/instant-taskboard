from pathlib import Path
from PIL import Image, ImageDraw
p=Path('src/Taskboard.Desktop/Assets'); p.mkdir(exist_ok=True)
img=Image.new('RGBA',(1024,1024)); d=ImageDraw.Draw(img)
d.ellipse((12,12,1012,1012),fill=(25,35,51,220),outline=(74,96,120,255),width=16)
for points in [[(512,260),(764,512),(512,764),(260,512),(512,260)],[(512,373),(651,512),(512,651),(373,512),(512,373)]]:
    d.line(points,fill=(150,218,204,255),width=32,joint='curve')
img.save(p/'orb.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
