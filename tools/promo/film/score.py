"""Original 32-second score and motion sound design; all sounds synthesized.
No samples, songs, or third-party recordings. Deterministic stereo 48 kHz.
"""
from pathlib import Path
import numpy as np
from scipy.signal import butter, sosfilt
from scipy.io.wavfile import write

SR=48000
rng=np.random.default_rng(42)
mix=np.zeros((SR*32,2),np.float64)
def filt(x,hz,kind='lowpass'):
    return sosfilt(butter(2,hz,fs=SR,btype=kind,output='sos'),x)
def add(x,at,gain=1,pan=0):
    start=int(at*SR)
    if start<0 or start>=len(mix): return
    n=min(len(x),len(mix)-start)
    p=(pan+1)*np.pi/4
    mix[start:start+n,0]+=x[:n]*gain*np.cos(p)
    mix[start:start+n,1]+=x[:n]*gain*np.sin(p)
def hz(m):return 440*2**((m-69)/12)
def pluck(m,length=2.1):
    t=np.arange(int(length*SR))/SR; f=hz(m)
    y=np.sin(2*np.pi*f*t)*np.exp(-t*3.4)
    y+=.3*np.sin(2*np.pi*2*f*t)*np.exp(-t*7)
    y+=.12*np.sin(2*np.pi*3*f*t)*np.exp(-t*12)
    return y*(1-np.exp(-t*180))*.35
def chord(notes,at,duration,gain):
    t=np.arange(int(duration*SR))/SR
    env=np.minimum(1,t/.8)*np.minimum(1,(duration-t)/1.7)
    for i,m in enumerate(notes):
        f=hz(m);s=(np.sin(2*np.pi*f*.998*t)+np.sin(2*np.pi*f*1.002*t))*.20
        s+=np.sin(2*np.pi*f*2*t)*.045
        add(s*env,at,gain,(i/(len(notes)-1)-.5)*1.4)
def whoosh(at,duration,gain,pan=0):
    t=np.arange(int(duration*SR))/SR
    s=filt(filt(rng.normal(0,1,len(t)),250,'highpass'),4500)
    env=np.sin(np.pi*t/duration)**2
    add(s*env,at,gain,pan)
def kick(at,gain):
    t=np.arange(int(.45*SR))/SR
    phase=2*np.pi*(48*t+80*.026*(1-np.exp(-t/.026)))
    s=np.sin(phase)*np.exp(-t*12)*(1-np.exp(-t*500))
    add(s,at,gain)
def hat(at,gain,pan):
    t=np.arange(int(.08*SR))/SR
    s=filt(rng.normal(0,1,len(t)),7500,'highpass')*np.exp(-t*70)
    add(s,at,gain,pan)
def click(at,gain=.035):
    t=np.arange(int(.055*SR))/SR
    add(np.sin(2*np.pi*1600*t)*np.exp(-t*125),at,gain)

# D major / B minor: open ninths with room for the product's sound effects.
chords=[[50,57,61,66,69],[47,54,57,61,66],[43,50,57,59,66],[45,52,57,61,64]]
chord(chords[0],0,6,.27)
for i,start in enumerate([5,9,13,17,21]):chord(chords[i%4],start,5.7,.34)
chord([38,50,57,61,66,73],26,6,.43)
for i,t0 in enumerate(np.arange(5,25.9,.25)):
    bar=int((t0-5)//4)%4
    notes=[n+12 for n in chords[bar][1:]]
    note=notes[[0,2,1,3,1,2,0,3][i%8]%len(notes)]
    g=.09 if t0<14 else .115
    s=pluck(note)
    add(s,t0,g,(-.5,.35,-.25,.5)[i%4])
    add(s,t0+.375,g*.18,-.45 if i%2 else .45)
    if i%2==0:
        t=np.arange(int(.42*SR))/SR; f=hz(chords[bar][0]-12)
        add(np.sin(2*np.pi*f*t)*(1-np.exp(-t*90))*np.exp(-t*8),t0,.15)
for i,t0 in enumerate(np.arange(5,26,.5)):
    kick(t0,.20 if t0<14 else .26)
    hat(t0+.25,.016 if t0<14 else .025,(-.4,.4)[i%2])
    if i%2:
        n=int(.16*SR);t=np.arange(n)/SR
        snap=filt(rng.normal(0,1,n),1100,'highpass')*np.exp(-t*34)
        add(snap,t0,.043)
# Quiet, tactile motion accents rather than stock trailer impacts.
whoosh(.1,1.8,.055,-.3)
whoosh(4.35,.65,.12,.25)
whoosh(12.9,1.1,.13,-.2)
whoosh(20.45,.55,.11,.3)
whoosh(25.2,.8,.11,-.2)
for t0 in [7.8,9.25,10.7,12.2,22.72]:click(t0)
for i,(t0,m) in enumerate([(26,62),(26.25,66),(26.5,69),(27,73)]):
    s=pluck(m,4.5);add(s,t0,.34,(-.3,-.1,.1,.3)[i]);add(s,t0+.375,.10,-.3 if i%2 else .3)

# A small stereo feedback delay gives the dry synthesis a coherent acoustic space.
dry=mix.copy()
for delay,gain in [(.113,.14),(.173,.11),(.337,.09),(.521,.05),(.853,.035)]:
    n=int(delay*SR)
    mix[n:]+=dry[:-n,::-1]*gain
for c in range(2):mix[:,c]=filt(mix[:,c],28,'highpass')
mix=np.tanh(mix*1.35)
mix*=.85/max(np.max(np.abs(mix)),.01)
mix[:2400]*=np.linspace(0,1,2400)[:,None]
n=SR*2;mix[-n:]*=np.linspace(1,0,n)[:,None]**1.7
out=Path(__file__).resolve().parents[3]/'artifacts/promo/film/soundtrack.wav'
out.parent.mkdir(parents=True,exist_ok=True)
write(out,SR,(mix*32767).astype(np.int16))
print(out)
