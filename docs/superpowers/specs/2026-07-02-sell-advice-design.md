# LoL Advisor — Consejo de venta de items incoherentes (diseño)

**Fecha:** 2026-07-02
**Estado:** Diseño aprobado en sesión interactiva. A pedido del usuario, se implementa
**sin tests nuevos** (la suite existente se corre como regresión; el fixture de tests no
trae `gold.sell`, así que el comportamiento de los tests no cambia).

## Objetivo

Sugerir la venta de items ya comprados que dejaron de ser coherentes con la build
actual (detectada o forzada con el override de rol) y que no cumplen un rol situacional
activo. Caso típico: cambiaste el selector a Mage y llevas Shurelya's del arranque
como support.

## Decisiones

- **Disparador:** siempre que exista un candidato (no se exige inventario lleno ni que
  la venta desbloquee una compra).
- **Métrica:** fit relativo = `fit(mi arquetipo) / mejor fit entre los 7 arquetipos`
  (mismo cálculo que la detección de build por inventario). Candidato si
  `< SellFitRatioThreshold` (config, default 0.5).
- **Elegibilidad:** item final del inventario (no componente/botas/consumible/Trinket/
  GoldPer), `GoldTotal >= MinCompletedItemGold`, `SellGold > 0`, dedupe por nombre.
- **Salvaguardas situacionales (nunca vender):** anti-heal si el sustain enemigo supera
  el umbral del modo; QSS/cleanse si hay supresión; rompe-escudos si tienen escudos;
  Armor/SpellBlock si el daño enemigo está sesgado (`SkewedDamageShare`) a ese tipo.
- **Tope:** máximo 2 sugerencias, ordenadas por oro de venta. Si la venta combinada
  cubre el oro que falta para el item top recomendado, la razón lo dice
  ("selling funds X now").

## Piezas

- `StaticItem.SellGold` ← `gold.sell` de ddragon (Gold DTO + `FromJson`).
- `SellSuggestion(Item, SellGold, Reason)` + `ItemAdvicePlan.Sells`.
- `ItemsConfig.SellFitRatioThreshold = 0.5`.
- `ItemAdvisor.SellSuggestions(...)` — usa los pesos por arquetipo ya existentes y el
  `TeamThreat` del tick; corre con el perfil ya resuelto (override incluido).
- UI: línea ámbar tipo botas — `Sell: Shurelya's Battlesong (+1,540 g) — support item,
  doesn't fit your mage build`; oculta si no hay candidatos (`MainViewModel.SellLine`).
