"""Generate original Lite Link icon candidates (Pillow; no external artwork)."""
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import math
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets'/'icons'; OUT.mkdir(parents=True,exist_ok=True)
S=4
def build(name,bg,accent,kind):
    im=Image.new('RGB',(512*S,512*S),bg); d=ImageDraw.Draw(im)
    parts=[f'<svg xmlns="http://www.w3.org/2000/svg" width="512" height="512" viewBox="0 0 512 512"><rect width="512" height="512" rx="108" fill="{bg}"/>']
    def rr(box,r,fill,stroke=None,width=1):
        d.rounded_rectangle(tuple(int(x*S) for x in box),int(r*S),fill,stroke,width*S)
        x,y,x2,y2=box
        parts.append(f'<rect x="{x}" y="{y}" width="{x2-x}" height="{y2-y}" rx="{r}" fill="{fill or "none"}"'+(f' stroke="{stroke}" stroke-width="{width}"' if stroke else '')+'/>')
    def dot(x,y,r,fill):
        d.ellipse(((x-r)*S,(y-r)*S,(x+r)*S,(y+r)*S),fill)
        parts.append(f'<circle cx="{x}" cy="{y}" r="{r}" fill="{fill}"/>')
    def line(points,color,width):
        d.line([(int(x*S),int(y*S)) for x,y in points],fill=color,width=width*S,joint='curve')
        for x,y in (points[0],points[-1]): d.ellipse(((x-width/2)*S,(y-width/2)*S,(x+width/2)*S,(y+width/2)*S),fill=color)
        coords=' '.join(f'{x},{y}' for x,y in points)
        parts.append(f'<polyline points="{coords}" fill="none" stroke="{color}" stroke-width="{width}" stroke-linecap="round" stroke-linejoin="round"/>')
    def arc(cx,cy,r,start,end,color,width):
        d.arc(((cx-r)*S,(cy-r)*S,(cx+r)*S,(cy+r)*S),start,end,fill=color,width=width*S)
        a,b=map(math.radians,(start,end));x1,y1=cx+r*math.cos(a),cy+r*math.sin(a);x2,y2=cx+r*math.cos(b),cy+r*math.sin(b)
        parts.append(f'<path d="M{x1},{y1} A{r},{r} 0 0 1 {x2},{y2}" fill="none" stroke="{color}" stroke-width="{width}"/>')
    if kind=='signal':
        rr((80,218,432,402),70,'#123B3E',accent,12)
        line([(131,306),(193,306)],accent,15);line([(162,275),(162,337)],accent,15)
        for x,y in [(350,280),(378,308),(322,308),(350,336)]: dot(x,y,11,accent)
        dot(244,353,10,accent);dot(276,353,10,accent)
        arc(256,237,132,220,320,accent,16);arc(256,237,85,220,320,accent,15);dot(256,197,10,accent)
    elif kind=='link':
        # Interlocking rounded links; original geometry, not a service logo.
        rr((89,151,286,352),66,None,accent,24)
        rr((226,151,423,352),66,None,'#F3EEFF',24)
        rr((230,225,282,277),16,bg)
        line([(218,251),(294,251)],accent,20)
        dot(179,251,11,'#F3EEFF');dot(333,251,11,accent)
        line([(176,402),(336,402)],accent,12)
    elif kind=='sunrise':
        dot(256,246,172,'#F49755');dot(256,246,144,'#FFB578')
        rr((94,225,418,388),66,'#FFF1DF')
        line([(137,303),(197,303)],'#6A352C',14);line([(167,273),(167,333)],'#6A352C',14)
        for x,y in [(345,277),(372,304),(318,304),(345,331)]:dot(x,y,10,'#6A352C')
        arc(256,227,97,220,320,'#FFF1DF',13);arc(256,227,59,220,320,'#FFF1DF',13)
    else:
        rr((54,160,458,368),66,accent)
        rr((140,185,372,343),20,'#101D38')
        line([(77,263),(119,263)],'#101D38',11);line([(98,242),(98,284)],'#101D38',11)
        for x,y in [(413,239),(435,263),(391,263),(413,287)]:dot(x,y,8,'#101D38')
        line([(190,287),(190,238),(230,238)],accent,13)
        line([(269,238),(309,238),(309,287)],'#F0F6FF',13)
        line([(230,263),(269,263)],'#F0F6FF',13)
        arc(256,162,91,225,315,accent,12)
    parts.append('</svg>')
    im=im.resize((512,512),Image.Resampling.LANCZOS)
    im.save(OUT/f'{name}.png');im.save(OUT/f'{name}.jpg',quality=96,subsampling=0)
    im.save(OUT/f'{name}.ico',sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
    (OUT/f'{name}.svg').write_text('\n'.join(parts),encoding='utf-8')
    return im
specs=[('A-mint-signal','#0A1924','#4DE0B9','signal'),('B-lilac-link','#261D43','#BD9AFF','link'),('C-sunrise-pad','#7C3F33','#FFB578','sunrise'),('D-blue-console','#101D38','#81B7FF','console')]
sheet=Image.new('RGB',(1440,500),'#0B1220');canvas=ImageDraw.Draw(sheet)
try: font=ImageFont.truetype('arial.ttf',23)
except OSError: font=ImageFont.load_default(size=23)
for i,spec in enumerate(specs):
    icon=build(*spec);x=32+i*354;sheet.paste(icon.resize((320,320)),(x,48));canvas.text((x,394),spec[0].replace('-',' '),font=font,fill='#EAF0F6')
sheet.save(OUT/'candidates.png')
print(OUT)
