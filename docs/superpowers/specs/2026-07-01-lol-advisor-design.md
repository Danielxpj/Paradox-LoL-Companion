# LoL Advisor — Diseño (v1, "primer acercamiento")

**Fecha:** 2026-07-01
**Estado:** Aprobado por el usuario (secciones 1 y 2). Sección 3 definida por defecto.

## Objetivo

Herramienta de escritorio en **C# / WPF** que **lee los datos que entregan las APIs locales de League of Legends** y los muestra en un dashboard estilo *Google Data Studio / Looker*. Enfoque **datos primero**: leer y mostrar todo lo posible; los **consejos** son una capa fina inicial (pocas reglas) que se ampliará después.

## Alcance v1

- **Fuentes de datos:**
  - **Live Client Data API** (`https://127.0.0.1:2999/liveclientdata/...`, sin auth): se lee el payload completo (`allgamedata`).
  - **LCU API** (cliente): solo **champ select + invocador/lobby actual** (no "todos" los endpoints).
- **Consejos (capa fina):** oro-para-item, timer de objetivo, CS/min vs. referencia, 1–2 tips de draft.
- **UI:** dashboard de tarjetas (scorecards + paneles) + **panel de consola** abajo con el stream crudo.
- **No incluido en v1:** motor de IA, cuentas Riot API (historial), overlay in-game, base de datos.

## Sección 1 — Arquitectura por capas

Piezas chicas, cada una con una sola responsabilidad, testeables por separado.

1. **Conectores (lectores de datos)**
   - `LiveClientConnector`: polling a `allgamedata` cada ~1s, ignora el certificado autofirmado (solo `127.0.0.1`), deserializa al modelo. Resiliente a "no hay partida".
   - `LcuConnector`: descubre el `lockfile` → puerto + token → auth básica + WebSocket para eventos de champ select. Lee sesión de champ select + invocador actual.
2. **Modelo de dominio** — POCOs que mapean el JSON: `GameState` (activePlayer, allPlayers, events, gameStats) y `ChampSelectState`.
3. **Motor de consejos (fino)** — reglas `IAdviceRule` evaluadas contra el estado → `AdviceItem` (categoría, severidad, mensaje). Fácil de ampliar.
4. **Canal de consola/crudo** — bus que captura payload crudo + parseado + consejos → panel de consola (+ opcional a archivo).
5. **UI WPF (MVVM)** — los conectores viven en librería sin UI para poder testearlos.

**Estructura de proyectos:**
- `LoLAdvisor.Core` (classlib, `net10.0`): modelos, conectores, motor de consejos, canal de log. Sin UI.
- `LoLAdvisor.App` (WPF, `net10.0-windows`): ventana, MVVM, cableado (DI simple).
- `LoLAdvisor.Tests` (xUnit): fixtures (JSON grabados) + tests.

## Sección 2 — UI / Dashboard (estilo Data Studio)

**A) Header:** título + estado **Live Client** (🟢 conectado / 🟡 esperando) y **LCU** (🟢 / ⚪ cerrado); tiempo de partida y contexto ("En partida" / "Champ Select").

**B) Zona central — grilla de tarjetas, según contexto:**
- *En partida:* scorecards (Oro · CS y CS/min · KDA · Nivel · Tiempo), panel de **timers de objetivos** (Dragón/Heraldo/Barón), **tabla de jugadores** (campeón, equipo, nivel, KDA, items), **feed de consejos** (color por severidad).
- *Champ select:* picks/bans por equipo, tu selección, 1–2 tips de draft.
- Cambio de contexto **automático** (con toggle manual).

**C) Panel de consola (abajo, colapsable):** monospace, autoscroll, stream crudo + eventos parseados + consejos; botones pausar/limpiar/**guardar a archivo**.

**Estética:** fondo `#F5F6F8`, tarjetas blancas con borde/sombra sutil y esquinas redondeadas, acentos azul/verde/ámbar/rojo suaves, tipografía limpia, mucho aire.

## Sección 3 — Flujo de datos, errores y testing

**Flujo de datos:**
- `LiveClientConnector` corre un loop async cada ~1000 ms: `GET allgamedata` → deserializa → dispara `GameStateUpdated(GameState, rawJson)`.
- La App se suscribe; en el hilo de UI (`Dispatcher`) actualiza los ViewModels (scorecards, timers, tabla, consejos).
- El JSON crudo + resumen parseado + consejos emitidos van al `ConsoleLog` (colección observable / canal) → panel inferior.
- `AdviceEngine.Evaluate(GameState)` devuelve `IReadOnlyList<AdviceItem>` cada tick; el feed muestra el conjunto actual (dedupe por clave).
- `LcuConnector`: al inicio y periódicamente intenta ubicar el `lockfile`. Si aparece, conecta y se suscribe al WebSocket de champ select → `ChampSelectUpdated`. Si desaparece → desconectado.

**Manejo de errores:**
- Live Client no corre → conexión rechazada → estado "esperando partida", *backoff* (~2s), sin crashear.
- Certificado autofirmado → `HttpClientHandler` con validación custom que acepta solo `127.0.0.1`.
- JSON malformado/parcial → capturar, loguear al panel, saltar el tick.
- `lockfile` no encontrado → LCU "cerrado", reintentar.
- Conectores en background; updates a la UI vía `Dispatcher`.

**Timers de objetivos (v1, simple):** a partir de `eventdata` (DragonKill/HeraldKill/BaronKill con `EventTime`) se muestra "último tomado" + estimación de reaparición con defaults del parche actual (claramente marcados como estimación, configurables). No se pretende exactitud perfecta en v1.

**Testing:**
- `Core` es sin-UI → tests xUnit.
- **Fixtures:** `allgamedata.json` grabado en el proyecto de Tests → test de deserialización → mapeo a `GameState`.
- **Reglas de consejo:** construir `GameState` y afirmar los `AdviceItem` esperados (oro ≥ costo → consejo; CS/min bajo → consejo).
- **Modo replay:** alimentar un fixture por el pipeline para desarrollar la UI **sin tener LoL abierto**.

## Riesgos / notas

- Los timers de objetivos dependen del parche; se hardcodean defaults y se marcan como estimación.
- La conexión LCU (lockfile + cert + websocket) es la parte más frágil; se aísla en su conector y degrada a "cerrado" sin afectar el resto.
- Se prioriza que la app **nunca crashee** si el juego no está corriendo.

## Roadmap posterior (fuera de v1)

- Reglas de consejo más ricas / IA.
- Riri API (historial, post-partida).
- Overlay in-game.
- Persistencia / analítica de partidas.
