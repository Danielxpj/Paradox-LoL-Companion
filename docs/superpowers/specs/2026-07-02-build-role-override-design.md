# LoL Advisor — Override manual de rol/build (diseño)

**Fecha:** 2026-07-02
**Estado:** Diseño aprobado por el usuario en sesión interactiva.

## Objetivo

Hoy el arquetipo de build sale de `ChampionProfiler.ProfileWithInventory` (tag del
campeón + inventario real). Si el jugador quiere una build distinta a la detectada
(p.ej. Morgana **maga** en vez de support), el asesor no tiene forma de saberlo hasta
que ya compró ítems de esa build. Se agrega un **selector manual de rol** en la UI:
una fila de radio buttons que fuerza el arquetipo y hace que las recomendaciones de
ítems, botas y la guía de augments de Mayhem sigan la elección del jugador.

## Decisiones tomadas con el usuario

- **Opciones:** `Auto` + los 7 arquetipos internos, 1:1 con `BuildArchetype`
  (Marksman, Mage, Assassin, Fighter, AP Fighter, Tank, Support). Labels en inglés
  (preferencia global de UI).
- **Persistencia:** vuelve a `Auto` en cada partida (al desconectarse el Live Client
  o al alternar modo replay). El override es una decisión por partida.

## Diseño

### Core (`LoLAdvisor.Core`) — advisors stateless, override por parámetro

- `ItemAdvisor.Advise(GameState state, BuildArchetype? forcedArchetype = null)`:
  - `forcedArchetype == null` → comportamiento actual (`ProfileWithInventory`).
  - Con valor → `_profiler.Profile(me) with { Archetype = forced }`: se **salta la
    detección por inventario** (la elección manual manda sobre lo comprado).
  - El **perfil de daño no cambia**: es del kit del campeón, no de la build. Morgana
    maga o support sigue siendo daño mágico; el override cambia qué bolsa de ítems
    se puntúa (pesos por arquetipo, botas por defecto, guía de augments).
  - Banner: con override, el summary agrega `| Build override: mage` (en lugar de
    `| Build detected from your items: …`).
- `MayhemAdvisor.Advise(GameState state, BuildArchetype? forcedArchetype = null)`:
  la línea "As a …, prioritize …" y el consejo de supervivencia (`IsSquishy`) usan
  el arquetipo forzado.
- Se descartó guardar el override como estado del advisor (se recrean al recargar
  catálogo y lo perderían) o en `ItemsConfig.ArchetypeOverrides` (config persistente
  por campeón ≠ decisión por partida).

### UI (`LoLAdvisor.App`)

- En la tarjeta "Item advisor" (pestaña Match), bajo el encabezado:
  `Build:  ◉ Auto ○ Marksman ○ Mage ○ Assassin ○ Fighter ○ AP Fighter ○ Tank ○ Support`.
  ItemsControl horizontal de RadioButtons sobre una lista de opciones
  (label + `BuildArchetype?`), estilo del tema existente.
- `MainViewModel` guarda la selección (`BuildArchetype?`) y la pasa en cada tick a
  ambos advisors. Se cachea el último `GameState` para recalcular el consejo
  **al instante** al cambiar el radio (sin esperar el próximo tick).
- Reset a `Auto`: cuando el estado del Live Client deja de ser `Connected`
  (fin de partida) y al alternar replay (`SwitchLiveSource`).

### Tests

- Forzar `Mage` sobre una enchanter (p.ej. Karma/Soraka del TestCatalog) recomienda
  ítems de mago y botas de penetración mágica; el summary trae `Build override`.
- El override le gana a la detección por inventario (jugador con ítems de support +
  override Mage → recomendaciones de mago, sin `InferredFromItems`).
- `MayhemAdvisor` con override cambia la línea de arquetipo de la guía.
- El perfil de daño no se ve afectado por el override (los bonos de penetración
  siguen el kit del campeón).
- `forcedArchetype = null` conserva el comportamiento actual (default del parámetro;
  la suite existente lo cubre sin cambios).
