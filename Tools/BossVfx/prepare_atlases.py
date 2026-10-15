from PIL import Image
from pathlib import Path
import numpy as np
root=Path('Assets/_Project/Art/Vfx/Effekseer');keep=['Blow2','Claw1','Dark2','Gun3','Light1','Light4','Sword3']
for name in keep:
    p=root/(name+'.png');im=Image.open(p).convert('RGBA');fs=im.width//8
    # 统一裁剪全部帧，保持中心锚点不抖动。保留两像素透明边，避免相邻格采样。
    frames=[im.crop((f%8*fs,f//8*fs,(f%8+1)*fs,(f//8+1)*fs)) for f in range(64)]
    radius=8
    for f in frames:
        a=np.asarray(f)[:,:,3]; y,x=np.where(a>5)
        if len(x):radius=max(radius,int(max(abs(x-fs/2).max(),abs(y-fs/2).max()))+3)
    radius=min(fs//2,radius);out=Image.new('RGBA',(2048,2048))
    visible=[i for i,f in enumerate(frames) if np.asarray(f)[:,:,3].max()>5]
    if not visible: raise ValueError('Empty authored effect: '+name)
    first,last=visible[0],visible[-1]
    print(name, 'FirstFrame=',first,'FrameCount=',last+1)
    for i,f in enumerate(frames):
        f=f.crop((fs//2-radius,fs//2-radius,fs//2+radius,fs//2+radius)).resize((252,252),Image.Resampling.LANCZOS)
        if i>last:f=Image.new('RGBA',(252,252))
        elif i>=max(first,last-5):
            a=np.asarray(f).copy();a[:,:,3]=(a[:,:,3].astype(float)*max(0,(last-i)/5)).astype('uint8');f=Image.fromarray(a)
        out.paste(f,(i%8*256+2,i//8*256+2))
    out.save(p,optimize=True)
# 作者的这两张纹理原用于 Additive：黑底并非透明，转换成正常混合需要的 alpha。
for source,target in [('Line6','BeamTexture'),('Particle02','ProjectileTexture')]:
    a=np.asarray(Image.open('Assets/__BossVfxBake/Parts/'+source+'.png').convert('RGBA')).astype(float)/255
    bright=a[:,:,:3].max(axis=2)
    rgb=a[:,:,:3]/np.maximum(bright[:,:,None],.001)
    rgba=np.dstack((rgb,bright*a[:,:,3]))
    im=Image.fromarray((np.clip(rgba,0,1)*255).astype('uint8'))
    if target=='BeamTexture': im=im.transpose(Image.Transpose.ROTATE_90)
    im.save('Assets/_Project/Art/Vfx/'+target+'.png')
print('Prepared',len(keep),'clips')
