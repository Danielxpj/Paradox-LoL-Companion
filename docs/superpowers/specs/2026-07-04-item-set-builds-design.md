# Item set builds: 3 páginas de items desde OP.GG

**Fecha:** 2026-07-04 · **Estado:** aprobado

## Objetivo

Con las stats de OP.GG ya cacheadas por campeón, componer las **3 mejores builds**
y escribirlas automáticamente como **páginas de items** (item sets) en el cliente
de LoL, de modo que aparezcan en la tienda dentro de la partida. Las mismas listas
alimentan además el prior estadístico del asesor (hoy solo core + 4.º/5.º; se suma
el 6.º).

## Decisiones del usuario

- Destino: tienda del juego (escritura vía LCU) **y** refuerzo del algoritmo.
- Composición: **3 variantes del meta** — core fijo de 3 items + candidato
  *i*-ésimo de cada slot (4.º/5.º/6.º) para la variante *i*.
- Disparador: **automático** al detectar el campeón (sin botón).

## Arquitectura

Todo lo testeable vive en `ParadoxLoLCompanion.Core`; el ViewModel solo cablea.

### 1. Parser ampliado (`Core/Stats`)

- `ChampionBuildStats.SixthItems : IReadOnlyList<ItemSetStats>` — misma forma que
  `LateItems` (hoy 4.º/5.º). `OpggMcpClient.DesiredFields` suma
  `data.sixth_items[].{ids[],ids_names[],pick_rate,play,win}`.
- `ItemPriorFor` escanea también `SixthItems` → el bono estadístico del
  `ItemAdvisor` cubre más items sin tocar el scoring.
- **Asunción a verificar en el plan** (probe real): `sixth_items[]` tiene la misma
  forma compacta que `fourth_items[]`/`fifth_items[]`.

### 2. `ItemSetBuilder` (`Core/Stats`, puro)

Entrada: `ChampionBuildStats`, nombre del campeón, id numérico, mapa.
Salida: hasta 3 `ItemSetPage` (modelo propio, sin JSON).

- Variante *i* (0..2): bloques **Starter / Boots / Core / 4th pick / 5th pick /
  6th pick**; el slot usa su candidato *i*-ésimo y cae al mejor (índice 0) si no
  hay *i*-ésimo.
- Bloques sin datos se omiten; sin core no se genera ninguna página.
- Variantes duplicadas se descartan: si los slots no tienen suficientes candidatos
  distintos, se emiten menos de 3 páginas (nunca dos idénticas).
- Títulos: `Paradox: {Champ} #1..#3` (el prefijo `Paradox: ` identifica lo nuestro).

### 3. `LcuItemSetWriter` (`Core/Connectors/Lcu`)

Mismo patrón que `LcuRuneWriter` (nunca lanza; `null` = éxito, string = error UI):

1. `GET /lol-summoner/v1/current-summoner` → `summonerId`.
2. `GET /lol-item-sets/v1/item-sets/{summonerId}/sets` → documento actual.
3. Filtra los sets cuyo `title` empieza con `Paradox: ` y conserva el resto.
4. Agrega las 3 páginas nuevas (`associatedChampions=[champId]`,
   `associatedMaps=[mapa actual]`, `blocks[].items[]` con `id`/`count=1`).
5. `PUT` del documento completo.

**Regla de seguridad:** si el GET falla o no parsea, NO se hace PUT (el PUT
reemplaza todo el documento; escribir a ciegas destruiría páginas del usuario).

### 4. Disparadores (`MainViewModel`)

- **Champ select (principal):** al conocerse el campeón del jugador local vía LCU,
  pedir stats (posición = `assignedPosition`; mapa por `queueId`: 450/2400 → 12,
  resto → 11) y escribir las páginas — el juego las lee al arrancar la partida.
- **En vivo (refuerzo):** cuando `FetchStatsAsync` carga stats en partida (flujo
  actual), también escribe. En ARAM (banca, sin lock-in clásico) este es el camino
  normal.
- Deduplicación: no reescribir si campeón+mapa+parche no cambió (mismo patrón que
  `_statsFetchKey`).
- Log en consola: `[builds] 3 pages written for {champ}` / motivo del fallo.

## Manejo de errores

- Cliente cerrado → skip con log; se reintenta en el próximo disparo natural.
- GET de sets falla → no escribir, log del motivo.
- PUT rechazado → log con HTTP status.
- Datos parciales → páginas con los bloques disponibles; sin core → nada.

## Testing (TDD)

- `ItemSetBuilder`: 3 variantes bien compuestas; fallback de candidato faltante;
  bloques omitidos; títulos y asociaciones correctos; sin core → lista vacía.
- `LcuItemSetWriter.BuildSetsPayload` (interno estático): JSON exacto (title,
  blocks, items id/count, associatedChampions/Maps) y preservación de páginas
  ajenas + reemplazo de las propias.
- Parser: `sixth_items` poblado desde fixture; `ItemPriorFor` cubre el 6.º slot.
- Escritura real contra el cliente: smoke manual (requiere LoL abierto).

## Riesgos

1. No verificado que el juego recargue sets escritos con la partida ya empezada —
   mitigado con el disparador de champ select.
2. Forma de `sixth_items` asumida igual a 4.º/5.º — se confirma con probe antes de
   implementar.
3. En ARAM el disparo llega en partida (riesgo 1 aplica); si el juego no lo toma,
   la página queda para la siguiente partida con ese campeón.
