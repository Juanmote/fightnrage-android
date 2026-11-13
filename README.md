# Fight'N Rage para Android (launcher no oficial)

Launcher para jugar a **Fight'N Rage** en Android con tu propia copia del juego de PC (GOG o Steam).

Esta APK **no incluye el juego**: ni código, ni gráficos, ni música. Tienes que copiar
tus propios archivos de la versión de PC (`FIGHT_N_RAGE.exe` y la carpeta `data`).
Compra el juego para apoyar a su creador, Sebastián García (SebaGamesDev).

## Cómo se usa

1. Descarga la APK de la última versión en **Releases** e instálala.
2. Copia al móvil la carpeta del juego de PC (la que tiene `FIGHT_N_RAGE.exe` y `data`).
3. Abre la app → **Elegir carpeta del juego** → selecciona esa carpeta. La app copia los archivos a su almacenamiento.
4. (Opcional) **Importar partida de PC**: el `data.sav` de
   `%USERPROFILE%\AppData\LocalLow\sebagamesdev\gog\fightnrage\savedata\`.
5. **Jugar**.

Si algo falla, el registro está en `Android/data/com.juanmote.fightnrage/files/log.txt`.

## Estado

| | |
|---|---|
| Gráficos (OpenGL ES, shaders, efecto CRT) | ✅ probado en PC con el backend nuevo |
| Mando (Android) / teclado | ✅ implementado, pendiente de probar en consola |
| Partidas (compatibles con PC) | ✅ |
| Sonido (efectos y música con bucles) | ✅ probado en PC, pendiente de probar en consola |
| Controles táctiles | ⏳ fase 3 |

Detalles técnicos en [docs/ANALISIS.md](docs/ANALISIS.md).

## Estructura

- `src/FnrStub` — ensamblado de referencia escrito a mano (solo firmas de los tipos que usa el backend; sin código del juego).
- `src/FnrPort.Core` — backend: implementa las interfaces del motor del juego sobre OpenGL ES.
- `src/FnrPort.Desktop` — host SDL2 para probar en PC (Linux/Windows): `FnrPort.Desktop <carpeta del juego>`.
- `src/FnrPort.Android` — app de Android (launcher + actividad del juego).
- `src/ThirdParty/NVorbis` — decodificador OGG Vorbis (NVorbis 0.10.5, MIT).
