# Botas: prioridad op.gg con amenaza como nota

**Fecha:** 2026-07-08
**Estado:** aprobado

## Problema

Hoy `ItemAdvisor.BootsFor` decide las botas con esta prioridad:

1. Amenaza: Mercury's Treads (CC pesado o daño mágico dominante) o Plated
   Steelcaps (daño físico de autoataques) — retornan de inmediato.
2. Stats de op.gg: las botas más compradas por jugadores del campeón.
3. Arquetipo del campeón como último recurso.

El usuario quiere que las botas de la meta (op.gg) manden siempre que haya
datos, y que la lógica de amenaza no cambie la recomendación sino que aparezca
como nota dentro del texto del consejo.

## Decisión (elegida por el usuario)

**op.gg primero, amenaza como nota.** Cuando op.gg trae botas para el campeón,
esas son la recomendación. Si la amenaza sugería otra bota distinta, el texto
del consejo lo menciona como alternativa, sin cambiar la recomendación.

Opciones descartadas: op.gg reemplaza por completo la lógica de amenaza (se
pierde información útil); op.gg gana salvo amenaza extrema con umbral más alto
(complejidad extra sin pedido concreto).

## Diseño

Cambio único en `BootsFor` (`src/ParadoxLoLCompanion.Core/Items/ItemAdvisor.cs`):

1. La sugerencia por amenaza se calcula igual que hoy, pero no retorna de
   inmediato: queda guardada como candidata local `(StaticItem, string reason)`.
2. Si `stats?.Boots` trae ids, se toma **el primer id en el orden de op.gg**
   que exista en `FinishedBootsFor(mapNumber)` (hoy el código itera en orden
   del catálogo; se cambia a orden de op.gg, que refleja popularidad). Esa
   bota es la recomendación, con razón:
   `most common boots on your champion (36% pick rate)`.
   Si la candidata por amenaza existe y es una bota distinta (por `Id`), se
   añade la nota al final de la razón:
   `— but <razón de amenaza>: consider <nombre de la bota de amenaza>`.
3. Sin datos de op.gg (MCP caído, campeón sin stats, o sus botas no existen en
   el mapa actual): comportamiento idéntico al actual — amenaza → arquetipo.

**Sin cambios en:** `AdvisorConfig`, modelos (`BootsAdvice.Reason` ya es un
string que la UI muestra tal cual en `BootsSub`), XAML, `OpggResponseParser`.

## Tests

- Los tests existentes de Mercs/Steelcaps no pasan stats de op.gg, así que
  siguen válidos: ahora cubren el fallback sin datos.
- Nuevos en `ItemAdvisorStatsTests`:
  - Con stats de op.gg (p. ej. Sorcerer's Shoes) y amenaza de CC pesado:
    recomienda la bota de op.gg y la razón contiene la nota con Mercury's.
  - Con stats de op.gg y sin amenaza sesgada: recomienda la de op.gg sin nota
    (razón igual que hoy).
