# MATCH tab "Cockpit" — cero scroll en Full HD

**Fecha:** 2026-07-08
**Estado:** aprobado (mockups validados en el visual companion; opción C elegida)

## Problema

En 1920×1080 la pestaña MATCH no entra en pantalla: las filas de ADVICE ocupan
~90px cada una para una línea de texto (y 3 de 4 duplican las tarjetas de items),
el SUMMARY es una sección entera de 6 tarjetas grandes, y AUGMENTS es otra
sección aparte. PLAYERS queda siempre bajo el fold.

## Diseño (opción C "Cockpit")

Nueva estructura del MATCH tab, tres filas:

1. **Fila 1 — Grid 2*/1*:** ENEMY X-RAY (contenido actual, izquierda) +
   **SUMMARY mini** a la derecha: las 6 scorecards en UniformGrid 3×2 con
   tarjetas compactas (título chico, valor 18px, subrayado de acento 2px).
   Desaparece la sección SUMMARY grande.
2. **Fila 2 — Grid 2*/1*:** izquierda = ITEM ADVISOR completo (header, selector
   de build, 3 tarjetas de items) + **strip único** que fusiona BOOTS (icono,
   nombre, chip, razón en 1 línea con `TextTrimming` + ToolTip con el texto
   completo) + AUGMENTS (chip + status + pick-now; guidance pasa a ToolTip) +
   SKILLS (derecha). El strip se colapsa solo si BootsLine, MayhemStatus y
   SkillPriorityLine están TODOS vacíos (MultiDataTrigger); cada parte interna
   se colapsa individualmente. Sells y late tips quedan debajo (colapsables,
   normalmente vacíos). Derecha = **rail de ADVICE**: header + filas compactas
   (barra de severidad 3px, categoría chica y mensaje 12px en pocas líneas,
   padding 8,6).
3. **Fila 3 — PLAYERS a lo ancho**, denso: DataGrid FontSize 12, RowHeight 26,
   iconos de campeón/items más chicos.

**Feed dedup (ViewModel):** `RebuildAdvice` filtra `AdviceCategory.Items` —
esos consejos duplican textualmente las tarjetas del panel. El feed queda para
Gold/Farming/General.

**Intocables:** la barra inferior de la consola (RAW DATA CONSOLE + botones),
el ScrollViewer (queda como red de seguridad), la pestaña CHAMP SELECT.

Presupuesto de altura (1080p, ~1040px útiles): header+tabs ~130, fila 1 ~150,
fila 2 ~330, fila 3 restante (~360) + footer consola ~40.

## Tests

Cambio de layout (XAML) sin lógica nueva de Core: la suite completa actúa de
regresión. El filtro de `RebuildAdvice` vive en el App (sin proyecto de tests
de UI). Verificación visual: lanzar el exe en Replay y capturar screenshot
(flujo documentado con System.Windows.Automation) confirmando que no aparece
la scrollbar vertical.
