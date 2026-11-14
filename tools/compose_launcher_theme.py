"""Tema del launcher de Fight'N Rage: rock de peli de acción de los 80, compuesto y sintetizado desde cero.

"Last Street Standing" — Re menor, 138 BPM, 36 compases en bucle (~63 s):
  intro (4) riff con guitarra muteada y redoble · estrofa (8) riff + melodía de sintetizador
  estribillo (8) metales de sintetizador + guitarra solista · puente/solo (8) · vuelta (4) · cierre en La (4)
Instrumentos: dos guitarras rítmicas saturadas (izquierda/derecha), bajo, batería con reverb "gated"
de los 80 en la caja, sintetizador de metales, guitarra solista con vibrato y deslices.
Uso: python3 compose_launcher_theme.py salida.wav
"""
import sys
import numpy as np
from scipy import signal

SR = 44100
BPM = 138
BEAT = 60.0 / BPM
STEP = BEAT / 4                     # semicorchea
BARS = 36
TOTAL = BARS * 16 * STEP
N = int(TOTAL * SR)
PAD = int(4 * SR)                   # cola para el bucle (se suma al principio)
L = np.zeros(N + PAD)
R = np.zeros(N + PAD)
rng = np.random.default_rng(1987)


def hz(m):
    return 440.0 * 2 ** ((m - 69) / 12.0)


def t_of(bar, step=0.0):
    return (bar * 16 + step) * STEP


def add(buf, start, sig, gain=1.0):
    i = int(round(start * SR))
    if i >= len(buf):
        return
    j = min(len(buf), i + len(sig))
    buf[i:j] += sig[: j - i] * gain


def stereo(start, sig, gain, pan):
    """pan -1 izquierda .. 1 derecha (potencia constante)."""
    a = (pan + 1) * np.pi / 4
    add(L, start, sig, gain * np.cos(a))
    add(R, start, sig, gain * np.sin(a))


def saw(freq, n, phase=None):
    f = np.broadcast_to(np.asarray(freq, dtype=float), (n,))
    ph = (rng.random() if phase is None else phase) + np.cumsum(f) / SR
    return 2 * (ph % 1.0) - 1


def adsr(n, a=0.005, d=0.1, s=0.7, r=0.05, gate=None):
    t = np.arange(n) / SR
    e = np.where(t < a, t / max(a, 1e-6), s + (1 - s) * np.exp(-(t - a) / d))
    g = n if gate is None else min(n, int(gate * SR))
    if g < n:
        e[g:] *= np.exp(-(t[g:] - t[g]) / r)
    return e


def lp(x, fc, order=2):
    b, a = signal.butter(order, min(fc / (SR / 2), 0.99), 'low')
    return signal.lfilter(b, a, x)


def hp(x, fc, order=2):
    b, a = signal.butter(order, fc / (SR / 2), 'high')
    return signal.lfilter(b, a, x)


def bp(x, f1, f2, order=2):
    b, a = signal.butter(order, [f1 / (SR / 2), f2 / (SR / 2)], 'band')
    return signal.lfilter(b, a, x)


def peak(x, f0, gain_db, q=1.0):
    A = 10 ** (gain_db / 40); w = 2 * np.pi * f0 / SR; al = np.sin(w) / (2 * q)
    b = [1 + al * A, -2 * np.cos(w), 1 - al * A]; a = [1 + al / A, -2 * np.cos(w), 1 - al / A]
    return signal.lfilter(b, a, x)


# ---------------------------------------------------------------- guitarra rítmica
def power_chord(root, dur, mute=False, side=0):
    n = int((dur + (0.03 if mute else 0.25)) * SR)
    notes = [root, root + 7, root + 12]
    x = np.zeros(n)
    for m in notes:
        for det in (-0.07, 0.07):
            x += saw(hz(m) * 2 ** (det / 12), n)
    x = hp(x, 90)
    x = lp(x, 1200 if mute else 3200)
    drive = 9.0 if mute else 7.0
    x = np.tanh(x * drive)
    x = peak(x, 1800, 5, 0.8)              # "pantalla" del ampli
    x = peak(x, 300, -4, 1.0)
    x = hp(x, 130)
    x = lp(x, 5200, 4)
    env = adsr(n, 0.002, 0.05 if mute else 0.4, 0.15 if mute else 0.75, 0.03, gate=dur)
    return x * env


def rhythm(bar, step, length, root, mute=False, gain=0.12):
    dur = length * STEP
    for side, pan, late in ((0, -0.75, 0.0), (1, 0.75, 0.006)):
        stereo(t_of(bar, step) + late, power_chord(root, dur, mute, side), gain, pan)


# ---------------------------------------------------------------- bajo
def bass_note(m, dur, gain=0.33):
    n = int((dur + 0.05) * SR)
    x = saw(hz(m), n) * 0.7 + np.sign(np.sin(2 * np.pi * hz(m - 12) * np.arange(n) / SR)) * 0.4
    x = lp(x, 700) + lp(np.tanh(x * 3), 1800) * 0.25
    return x * adsr(n, 0.003, 0.15, 0.6, 0.03, gate=dur) * gain


def bass(bar, step, length, m):
    add(L, t_of(bar, step), bass_note(m, length * STEP), 0.85)
    add(R, t_of(bar, step), bass_note(m, length * STEP), 0.85)


# ---------------------------------------------------------------- batería
def kick():
    n = int(0.35 * SR); t = np.arange(n) / SR
    f = 45 + 110 * np.exp(-t / 0.03)
    x = np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.16)
    click = hp(rng.standard_normal(n), 2000) * np.exp(-t / 0.004) * 0.3
    return np.tanh((x + click) * 1.6)


def snare():
    n = int(0.45 * SR); t = np.arange(n) / SR
    tone = np.sin(2 * np.pi * 185 * t) * np.exp(-t / 0.05) * 0.6
    noise = bp(rng.standard_normal(n), 900, 7000) * np.exp(-t / 0.09)
    # reverb "gated" de los 80: cola de ruido que se corta de golpe a los 0,22 s
    tail = bp(rng.standard_normal(n), 600, 5000) * 0.35 * (t < 0.22) * np.exp(-t / 0.4)
    return np.tanh((tone + noise + tail) * 1.4)


def hat(open_=False):
    n = int((0.3 if open_ else 0.06) * SR); t = np.arange(n) / SR
    return hp(rng.standard_normal(n), 7000) * np.exp(-t / (0.12 if open_ else 0.015))


def crash():
    n = int(2.2 * SR); t = np.arange(n) / SR
    return hp(rng.standard_normal(n), 4000) * np.exp(-t / 0.7)


def tom(f0):
    n = int(0.4 * SR); t = np.arange(n) / SR
    f = f0 * (1 + 0.5 * np.exp(-t / 0.03))
    return np.sin(2 * np.pi * np.cumsum(f) / SR) * np.exp(-t / 0.18)


KICK, SNARE, HAT, OHAT, CRASH = kick(), snare(), hat(), hat(True), crash()


def drum(bar, step, kind, gain=1.0):
    t = t_of(bar, step) + rng.normal(0, 0.0015)
    if kind == 'k': stereo(t, KICK, 0.42 * gain, 0)
    elif kind == 's': stereo(t, SNARE, 0.36 * gain, 0.05)
    elif kind == 'h': stereo(t, HAT, 0.10 * gain * rng.uniform(0.8, 1.0), 0.35)
    elif kind == 'o': stereo(t, OHAT, 0.10 * gain, 0.35)
    elif kind == 'c': stereo(t, CRASH, 0.16 * gain, -0.4)
    elif kind in ('t1', 't2', 't3'):
        f, p = {'t1': (220, -0.4), 't2': (160, 0.0), 't3': (110, 0.4)}[kind]
        stereo(t, tom(f), 0.4 * gain, p)


def beat_rock(bar, crash_on_one=False, fill=False):
    if crash_on_one: drum(bar, 0, 'c')
    for s in range(0, 16, 2):
        if not (fill and s >= 8): drum(bar, s, 'h' if s % 4 else 'h', 1.0 if s % 4 == 0 else 0.7)
    for s in (0, 6, 10):
        drum(bar, s, 'k')
    for s in (4, 12):
        if not (fill and s == 12): drum(bar, s, 's')
    if fill:
        for s, k in ((8, 's'), (10, 's'), (11, 't1'), (12, 't1'), (13, 't2'), (14, 't3'), (15, 't3')):
            drum(bar, s, k, 0.9)


# ---------------------------------------------------------------- metales de sintetizador
def brass(bar, step, length, notes, gain=0.05):
    dur = length * STEP
    n = int((dur + 0.4) * SR)
    x = np.zeros(n)
    for m in notes:
        for det in (-0.12, 0.0, 0.12):
            x += saw(hz(m) * 2 ** (det / 12), n)
    t = np.arange(n) / SR
    cutoff = 900 + 2600 * np.clip(t / 0.12, 0, 1) * np.exp(-t / 1.2)
    # filtro que se abre (barrido típico de metales de sintetizador): por tramos
    out = np.zeros(n); blk = 512
    zi = None
    for i in range(0, n, blk):
        b, a = signal.butter(2, min(cutoff[i] / (SR / 2), 0.99), 'low')
        if zi is None: zi = signal.lfilter_zi(b, a) * 0
        out[i:i + blk], zi = signal.lfilter(b, a, x[i:i + blk], zi=zi)
    out *= adsr(n, 0.04, 0.5, 0.8, 0.25, gate=dur)
    stereo(t_of(bar, step), out, gain, -0.3)
    stereo(t_of(bar, step) + 0.011, out, gain, 0.3)


# ---------------------------------------------------------------- guitarra solista
def lead_note(m, dur, slide_from=None, vib=0.0, bend=False):
    n = int((dur + 0.08) * SR); t = np.arange(n) / SR
    f = np.full(n, hz(m))
    if slide_from is not None:
        k = np.clip(t / 0.06, 0, 1); f = hz(slide_from) + (hz(m) - hz(slide_from)) * k
    if bend:
        f = f * 2 ** ((-1 + np.clip(t / 0.12, 0, 1)) / 12)   # sube un semitono hasta la nota
    if vib:
        f = f * (1 + vib * np.sin(2 * np.pi * 6.0 * t) * np.clip((t - 0.18) / 0.15, 0, 1))
    x = saw(f, n) + 0.5 * saw(f * 1.003, n)
    x = hp(x, 200); x = lp(x, 2500)
    x = np.tanh(x * 6)
    x = peak(x, 2400, 6, 1.2); x = lp(x, 6000, 4)
    return x * adsr(n, 0.004, 0.3, 0.8, 0.06, gate=dur)


def synth_note(m, dur):
    n = int((dur + 0.1) * SR); t = np.arange(n) / SR
    duty = 0.3 + 0.15 * np.sin(2 * np.pi * 0.7 * t)
    ph = (np.cumsum(np.full(n, hz(m))) / SR) % 1.0
    x = np.where(ph < duty, 1.0, -1.0) + 0.4 * saw(hz(m) * 1.004, n)
    x = lp(x, 3000)
    return x * adsr(n, 0.01, 0.2, 0.7, 0.08, gate=dur)


LEAD = []          # (compás, paso, largo, nota, opciones)


def lead(bar, items, kind='g'):
    s = 0.0
    for it in items:
        m, ln = it[0], it[1]
        opt = it[2] if len(it) > 2 else {}
        if m is not None:
            LEAD.append((bar, s, ln, m, opt, kind))
        s += ln
        while s >= 16: s -= 16; bar += 1


# ---------------------------------------------------------------- canción
D, F, G, A, Bb, C, E = 38, 41, 43, 45, 46, 48, 40   # raíces graves (Re2...)


def riff(bar):
    """Riff principal en Re: chugs muteados con golpes de Fa y Sol."""
    pat = [(0, 2, D, True), (2, 2, D, True), (4, 1, D, True), (5, 1, D, True), (6, 2, F, False),
           (8, 2, D, True), (10, 2, D, True), (12, 2, G, False), (14, 2, F, False)]
    for s, ln, r, mu in pat:
        rhythm(bar, s, ln, r, mu)
        bass(bar, s, ln, r)


def riff2(bar, r1, r2):
    """Compás con dos acordes sostenidos y chugs."""
    for s in (0, 2, 4):
        rhythm(bar, s, 2, r1, s > 0); bass(bar, s, 2, r1)
    rhythm(bar, 6, 2, r1, False); bass(bar, 6, 2, r1)
    for s in (8, 10, 12):
        rhythm(bar, s, 2, r2, s > 8); bass(bar, s, 2, r2)
    rhythm(bar, 14, 2, r2, False); bass(bar, 14, 2, r2)


def held(bar, r, beats=16):
    rhythm(bar, 0, beats, r, False, 0.11)
    for s in range(0, 16, 2): bass(bar, s, 2, r)


b = 0
# --- intro (0-3): guitarra muteada sola, entra la batería con redoble
for k in range(4):
    for s in range(0, 16, 2): rhythm(b + k, s, 2, D, True, 0.13)
for s in range(0, 16, 4): drum(b + 2, s, 'k')
for s in range(0, 16, 1): drum(b + 3, s, 's', 0.35 + s * 0.04)
b = 4
# --- estrofa (4-11): riff + melodía de sintetizador
for k in range(0, 8, 2):
    riff(b + k)
    if k < 6: riff2(b + k + 1, Bb, C)
    else: riff2(b + k + 1, Bb, A)
for k in range(8): beat_rock(b + k, crash_on_one=(k == 0), fill=(k == 7))
# melodía (Re menor heroico)
lead(b, [(62, 4), (65, 2), (69, 6), (67, 2), (65, 2),
         (67, 4), (65, 2), (64, 2), (62, 8),
         (62, 4), (65, 2), (69, 4), (72, 2), (70, 2), (69, 2),
         (67, 6), (69, 2), (65, 8),
         (62, 4), (65, 2), (69, 6), (67, 2), (65, 2),
         (67, 4), (69, 2), (70, 2), (72, 8),
         (74, 4), (72, 2), (70, 2), (69, 4), (67, 4),
         (69, 12, {'vib': 0.012}), (None, 4)], kind='s')
b = 12
# --- estribillo (12-19): Bb - C - Dm (x2) y Bb - C - F - A
prog = [(Bb, [58, 62, 65]), (C, [60, 64, 67]), (D, [62, 65, 69]), (D, [62, 65, 69]),
        (Bb, [58, 62, 65]), (C, [60, 64, 67]), (F, [60, 65, 69]), (A, [61, 64, 69])]
for k, (r, ch) in enumerate(prog):
    if k == 7:
        for s in range(0, 16, 2): rhythm(b + k, s, 2, A, s % 4 != 0); bass(b + k, s, 2, A)
    else:
        held(b + k, r)
    brass(b + k, 0, 16, ch, 0.045)
    beat_rock(b + k, crash_on_one=(k in (0, 4)), fill=(k == 7))
lead(b, [(74, 6, {'bend': True}), (72, 2), (70, 4), (72, 4),
         (72, 6), (70, 2), (69, 4), (67, 4),
         (69, 8, {'vib': 0.014}), (65, 4), (67, 4),
         (69, 12, {'vib': 0.016}), (None, 4),
         (74, 6, {'bend': True}), (72, 2), (70, 4), (74, 4),
         (76, 6), (74, 2), (72, 4), (76, 4),
         (77, 8, {'vib': 0.014}), (76, 4), (74, 4),
         (73, 12, {'vib': 0.018}), (None, 4)])
b = 20
# --- puente / solo (20-27): Gm - A, arpegios rápidos y guitarra solista
for k in range(8):
    r = G if k % 2 == 0 else A
    riff2(b + k, r, r if k % 4 != 3 else (Bb if r == G else A))
    beat_rock(b + k, crash_on_one=(k == 0), fill=(k == 7))
    brass(b + k, 0, 16, [55, 58, 62] if r == G else [57, 61, 64], 0.025)
solo = []
for k in range(8):
    if k % 2 == 0: arp = [67, 70, 74, 79, 74, 70]
    else: arp = [69, 73, 76, 81, 76, 73]
    if k >= 6: arp = [x + 2 if k == 7 else x for x in arp]
    seq = (arp * 3)[:16] if k < 4 else None
    if seq:
        lead(b + k, [(m, 1) for m in seq])
    elif k in (4, 5):
        lead(b + k, [(79 if k == 4 else 81, 6, {'bend': True}), (77 if k == 4 else 79, 2), (74 if k == 4 else 76, 2), (77 if k == 4 else 78, 2), (79 if k == 4 else 81, 4, {'vib': 0.015})])
    elif k == 6:
        lead(b + k, [(m, 1) for m in [79, 77, 76, 74, 76, 77, 79, 81, 82, 81, 79, 77, 76, 74, 73, 74]])
    else:
        lead(b + k, [(76, 2), (77, 2), (79, 2), (81, 10, {'vib': 0.02, 'from': 79})])
b = 28
# --- vuelta (28-31): riff con la melodía de la estrofa una octava arriba
for k in range(0, 4, 2):
    riff(b + k); riff2(b + k + 1, Bb, C)
for k in range(4): beat_rock(b + k, crash_on_one=(k == 0), fill=(k == 3))
lead(b, [(74, 4), (77, 2), (81, 6), (79, 2), (77, 2),
         (79, 4), (77, 2), (76, 2), (74, 8),
         (74, 4), (77, 2), (81, 4), (84, 2), (82, 2), (81, 2),
         (79, 6), (81, 2), (77, 8)])
b = 32
# --- cierre (32-35): golpes de Bb, C y A sostenido que vuelve al principio
for k, (r, ch) in enumerate([(Bb, [58, 62, 65]), (C, [60, 64, 67]), (A, [61, 64, 69]), (A, [61, 64, 69])]):
    if k < 3:
        for s, ln in ((0, 3), (3, 3), (6, 10)):
            rhythm(b + k, s, ln, r, False, 0.16); bass(b + k, s, ln, r)
            drum(b + k, s, 'k'); drum(b + k, s, 'c' if s == 0 else 's', 0.8)
        brass(b + k, 0, 16, ch, 0.04)
    else:
        for s in range(0, 16, 2): rhythm(b + k, s, 2, A, True, 0.12); bass(b + k, s, 2, A)
        for s in range(0, 16, 1): drum(b + k, s, 's', 0.3 + s * 0.04)
lead(b, [(74, 3), (76, 3), (77, 10), (76, 3), (77, 3), (79, 10), (81, 16, {'vib': 0.02}), (None, 16)])

# --- interpretar solista y sintetizador
for bar, s, ln, m, opt, kind in LEAD:
    dur = ln * STEP
    if kind == 's':
        x = synth_note(m, dur)
        stereo(t_of(bar, s), x, 0.12, -0.15)
        stereo(t_of(bar, s) + 0.18, x, 0.045, 0.5)        # eco
    else:
        x = lead_note(m, dur, slide_from=opt.get('from'), vib=opt.get('vib', 0.006), bend=opt.get('bend', False))
        stereo(t_of(bar, s), x, 0.15, 0.1)
        stereo(t_of(bar, s) + BEAT * 0.75, x, 0.045, -0.6)  # eco de corchea con puntillo

# ---------------------------------------------------------------- mezcla y bucle
mix = np.stack([L, R])
mix[:, :PAD] += mix[:, N:N + PAD]          # la cola del final suena al empezar el bucle
mix = mix[:, :N]


def reverb(x, wet=0.12):
    out = np.zeros_like(x)
    for d, g in ((1557, 0.78), (1617, 0.77), (1491, 0.79), (1422, 0.8), (1277, 0.81), (1356, 0.8)):
        a = np.zeros(d + 1); a[0] = 1; a[d] = -g
        out += signal.lfilter([1], a, x)
    out = lp(out / 6, 5000)
    for d in (225, 556):
        out = signal.lfilter(np.r_[-0.5, np.zeros(d - 1), 1], np.r_[1, np.zeros(d - 1), -0.5], out)
    return x + out * wet


mix = np.stack([reverb(mix[0]), reverb(mix[1])])
mix = hp(mix, 30)
# compresión suave y limitador
peak_ = np.max(np.abs(mix))
mix = mix / peak_ * 1.6
mix = np.tanh(mix) / np.tanh(1.6)
mix *= 0.89
out = (mix.T * 32767).astype(np.int16)

import wave
with wave.open(sys.argv[1] if len(sys.argv) > 1 else 'theme.wav', 'wb') as w:
    w.setnchannels(2); w.setsampwidth(2); w.setframerate(SR); w.writeframes(out.tobytes())
print('duración %.1f s' % (N / SR))
