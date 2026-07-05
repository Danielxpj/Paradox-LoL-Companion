# LoL Advisor — Asesor de Items v3: scoring difuso y coherente (diseño)

**Fecha:** 2026-07-03
**Estado:** Diseñado y ejecutado de forma autónoma bajo directiva `/goal`
("mejora el sistema para realmente aumentar el rendimiento… inventa un sistema de
puntajes para los items de acuerdo al arquetipo… basado en las pasivas del oponente y
cómo se armó el oponente… quizás lógica difusa… coherencia en cada item recomendado y
priorizado").

Evoluciona el motor de scoring de la v2 (`ItemAdvisor`) sin reescribir su arquitectura:
mismos límites (`ParadoxLoLCompanion.Core.Items`, sin UI, `ItemAdvicePlan` inmutable por tick).

## Auditoría crítica de la v2 (qué limita hoy la calidad de las recomendaciones)

1. **Umbrales duros = comportamiento de acantilado.** Cada bono situacional se dispara
   con un `>= umbral` binario:
   - Enemigo 61 % físico → **cero** señal de armadura; a 62 % → bono completo.
   - 149 de armadura enemiga → **cero** penetración; a 150 → bono completo.
   - Sustain 0.24 → **cero** anti-curación; a 0.25 → +2.0 de golpe.
   Consecuencia doble: (a) recomendaciones **incoherentes** cerca del borde, y
   (b) **inestables** tick a tick — que un enemigo compre un item cruza el umbral y
   la recomendación aparece/desaparece. El jugador ve la lista "temblar".
2. **"Pasivas del oponente" superficiales.** Solo 4 categorías booleanas por campeón
   (curador, escudo, CC pesado, supresión). Faltan amenazas de kit que cambian tu
   compra: **daño verdadero / % de vida máxima** (Vayne, Fiora, Kog'Maw, Gwen), **crit**
   de tiradores, **enganche duro / pick**.
3. **"Cómo se armó el oponente" superficial.** Solo se leen armadura y RM planas. No se
   ve: **anti-heal enemigo** (mató tu robo de vida), **crit enemigo** (pide armadura
   anti-crit), **tankiness enemiga** (pide % de vida / penetración / on-hit).
4. **Scoring aditivo sin capa de coherencia/prioridad.** Base y situacional se suman en
   escalas distintas; no hay explicación de *por qué* ese orden. El usuario pide
   explícitamente "coherencia en cada item recomendado y priorizado".

## Objetivo

Un **motor de puntaje difuso** que (1) reemplace los acantilados por transiciones
graduales, (2) entienda **más pasivas del oponente** y **más de su build**, y (3)
produzca un orden **coherente y explicado** — cada recomendación con su prioridad y la
razón dominante de por qué está donde está.

## Diseño

### A. Núcleo difuso — `ParadoxLoLCompanion.Core.Items.Fuzzy` (nuevo, puro, testeable)

Toolkit sin dependencias. Cada "grado" es una pertenencia ∈ [0,1].

- **Funciones de pertenencia:**
  - `Ramp(x, foot, shoulder)` — 0 bajo `foot`, 1 sobre `shoulder`, lineal en medio
    (creciente). `shoulder < foot` la vuelve decreciente (útil para "es frágil / poca
    resistencia").
  - `Triangle(x, foot, peak, shoulder)`, `Trapezoid(...)` — para "zona media".
  - `Falling(x, shoulder, foot)` — azúcar sobre `Ramp` invertida.
- **Combinadores:** `And(a,b…) = min` (conjunción prudente), `AndProduct(a,b) = a·b`
  (conjunción suave, cuando ambos deben ser altos y se quiere que el débil arrastre),
  `Or(a,b…) = max`, `Not(a) = 1−a`, `Clamp01`.
- **Regla de diseño:** los umbrales de la v2 se preservan como el **cruce μ≈0.5** de la
  rampa correspondiente; el **pie** de cada rampa se ancla en/por debajo del valor usado
  por los tests negativos de la v2, de modo que "no dispares por debajo del umbral" siga
  valiendo, pero ahora con una subida gradual en vez de un salto.

Defuzzificación: estilo Sugeno/peso — cada regla aporta `magnitud · grado`; el puntaje
final es la suma ponderada (ver C). No hace falta centroide: buscamos un ranking, no un
valor físico.

### B. Modelo de amenaza enriquecido — `TeamThreat` + `ThreatAnalyzer`

`TeamThreat` gana **grados difusos continuos** (además de los campos crudos actuales,
que se conservan para los mensajes y para no romper consumidores):

| Campo nuevo (∈[0,1]) | Significado | Fuente |
|---|---|---|
| `PhysicalSkew` / `MagicalSkew` | qué tan sesgado a físico/mágico va el daño | rampa sobre los shares |
| `ArmorStack` / `MrStack` | cuánto apila el enemigo la resistencia que te frena | rampa sobre armadura/RM compradas |
| `Sustain` (grado) | urgencia de anti-curación (relativa al umbral por mapa) | rampa sobre `SustainScore` |
| `Burst` (grado) | qué tan fed va el mejor asesino | rampa sobre `BurstScore` |
| `CritThreat` | riesgo de crítico (tiradores + items de crit enemigos) | kit marksman + `CritChance` de sus items |
| `PercentHpTrue` | daño que ignora resistencias (% vida / verdadero) | lista de kit curada |
| `HardEngage` | enganche/pick que te obliga a supervivencia (GA/Zhonya/QSS) | lista de kit curada |
| `EnemyAntiHeal` | el enemigo **ya** compró anti-heal contra vos | items enemigos con Heridas Graves |
| `EnemyTankiness` | cuánto HP+resistencias acumuló el equipo enemigo | stats de sus items |

**Nuevas listas de kit en `ItemsConfig`** (mismo patrón curado por `Key` de ddragon,
única fuente confiable sin bajar los spells de cada campeón):
`PercentHpTrueDamageChampions`, `HardEngageChampions`. (Se mantienen las existentes.)

**Nueva lectura de build enemiga** en `ThreatAnalyzer` (data-driven desde sus items):
- `EnemyAntiHeal`: algún enemigo con item `AppliesGrievousWounds` → tu robo de
  vida/sustain vale menos.
- `CritThreat`: suma de `CritChance` de items enemigos, combinada (fuzzy OR) con el peso
  de tiradores → pide armadura anti-crit.
- `EnemyTankiness`: HP + armadura + RM comprados, normalizado → pide penetración / on-hit
  / % de vida en tu ofensiva.

### C. Motor de scoring coherente — `ItemAdvisor.ScoreItem` v3

Estructura en tres capas, cada regla situacional acompañada de su **razón** y de una
**contribución** rastreada (para la prioridad y la categoría):

```
score(i) = 3 · Base(i)                         // fit de arquetipo × eficiencia (como v2)
         + Σ_k  Mag_k · μ_k · aplica_k(i)       // counters difusos (aditivos, graduales)
```

- **Base** = `√fit(i) · (0.8 + 0.4·min(eff,1.2))` — idéntica a v2 (la raíz comprime el
  apilado de tags para que un counter necesario pueda superar a stats crudos).
- **Counters difusos** (reemplazan los `if umbral` de v2 por `μ` continuo; cada uno
  emite razón solo si `μ ≥ gate`):
  1. **Anti-curación**: `μ = Sustain · Not(teamHasGw)`, con `Mag` mayor si además pega
     con tu perfil de daño (Morello AP / Recordatorio AD / Cota tanque). Se **atenúa**
     por `EnemyAntiHeal`… no: el anti-heal se refuerza igual; lo que se atenúa es el
     valor del **robo de vida propio** (ver counter 8).
  2. **Penetración de armadura**: `μ = ArmorStack`, si vos hacés físico y el item tiene
     `ArmorPenetration`.
  3. **Penetración mágica**: `μ = MrStack`, si vos hacés mágico y el item tiene
     `MagicPenetration`.
  4. **Armadura vs. físico**: `μ = PhysicalSkew`, escalado por `ReceptividadDefensiva`
     (tanque 1.2 / frágil 1.0 / bruiser 0.6), si el item tiene `Armor`.
  5. **RM vs. mágico**: `μ = MagicalSkew`, ídem, si el item tiene `SpellBlock`.
  6. **Anti-burst**: `μ = Burst`, si sos frágil y el item suma defensa del tipo del
     burst **+** ofensiva de tu perfil (Zhonya/Ángel/Fauces/Banshee puntúan naturalmente).
  7. **Anti-crit (NUEVO)**: `μ = CritThreat`, si el item reduce daño crítico
     (`ReducesCritDamage`, detectado por keyword como los otros flags) — Randuin/Corazón
     de Hielo suben cuando el rival apila crítico.
  8. **Robo de vida devaluado (NUEVO)**: si `EnemyAntiHeal` es alto, se **resta** parte
     del fit que aportan los tags de sustain del item (`LifeSteal`/`Omnivamp`) — dejar de
     recomendar vampirismo cuando el enemigo ya lo cortó. Razón: "el enemigo ya tiene
     anti-curación".
  9. **Anti-tanque ofensivo (NUEVO)**: `μ = EnemyTankiness`, si vos hacés daño y el item
     aporta penetración/`OnHit`/`%HP` → contra un equipo gordo, tu daño quiere penetrar,
     no más stats planos.
  10. **Supervivencia vs. enganche (NUEVO)**: `μ = HardEngage`, si sos frágil y el item
      da defensa activa/estática (GA, Zhonya, QSS, Banshee) → sobrevivir al pick.
  11. **Daño verdadero / % vida (NUEVO)**: `μ = PercentHpTrue` **atenúa** el bono de
      apilar una sola resistencia o HP puro (contra daño que las ignora, apilar un muro
      rinde menos) y refuerza levemente supervivencia mixta. Modelado como factor sobre
      los counters 4/5 y sobre el fit de `Health`.
  12. **Limpieza de supresión** y **rompe-escudos**: se conservan (booleanos por
      naturaleza; supresión y presencia de escudos son sí/no).

- **Restricciones (sin cambios):** nunca items poseídos ni su árbol; un solo item de
  Heridas Graves; nombres únicos; tope `MaxRecommendations`.

### D. Prioridad y categoría visibles (coherencia explicada)

`ItemRecommendation` gana (aditivo, no rompe consumidores):

- `double Priority` ∈ [0,1] — `score(i) / score(top)`, la fuerza relativa de la
  recomendación (el top es 1.0). Da el "y priorizado" del goal, legible.
- `RecommendationCategory Category` ∈ { `Core`, `Counter`, `Defense`, `Spike` } —
  derivada de **qué contribución dominó** el puntaje del item: si un counter situacional
  supera a la base → `Counter`/`Defense`; si domina la base de arquetipo → `Core`; si es
  comprable ya y su spike de afinidad fue decisivo → `Spike`. Explica *por qué* está en
  ese lugar del orden.

La UI (`ItemRecoRowViewModel`) muestra una insignia de prioridad/categoría junto al
costo. El feed (`ItemRecommendationRule`) no cambia su contrato.

## Coherencia con la v2 (compatibilidad y no-regresión)

- **Todos los tests de la v2 quedan verdes.** Los umbrales se preservan como cruces
  μ≈0.5 y los pies de las rampas se anclan en/por debajo de los valores de los tests
  negativos (`Aram_TriggersAntiHealEarlier`, `Boots_ArchetypeDefault_WhenNoSkew`, etc.).
- `BootsFor` **no** se difumina: elegir botas es una decisión discreta (una de pocas);
  los umbrales duros son apropiados ahí. El fuzzy se aplica donde hay un continuo real:
  el **puntaje de items**.
- Constantes calibradas por TDD: los tests afirman **selección** (top / contiene / no
  contiene), no puntajes exactos, así que hay libertad para las magnitudes mientras el
  resultado de selección se mantenga.

## Manejo de errores

Igual que v2: catálogo no cargado → nada; campeón no resuelto → perfil genérico; datos
parciales del Live Client → degrada a puntaje por arquetipo puro. Los grados difusos con
entradas en 0 dan μ=0 (sin bono, sin razón): la degradación es natural.

## Testing (TDD)

- `FuzzyMathTests` (nuevo): monotonía, saturación en [0,1], cruces, combinadores.
- `ThreatAnalyzerTests`: nuevos grados (skew/stack/sustain/burst continuos), `CritThreat`,
  `PercentHpTrue`, `HardEngage`, `EnemyAntiHeal`, `EnemyTankiness`; suma de shares = 1.
- `ItemAdvisorTests`: **propiedades** además de casos —
  - *Monotonía*: más armadura enemiga ⇒ el puntaje relativo de un item de penetración no
    baja; más sustain ⇒ el anti-heal no baja.
  - *Gradualidad/estabilidad*: cruzar el viejo umbral no cambia el **orden** de golpe
    (delta de puntaje acotado alrededor del cruce).
  - *Coherencia de prioridad*: `Priority` decreciente y `Top.Priority == 1`.
  - Nuevos counters: enemigo crit ⇒ sube armadura anti-crit; enemigo con anti-heal ⇒ baja
    el vampirismo recomendado; enemigo gordo ⇒ sube penetración/on-hit; kit de % vida ⇒
    no sobre-apila un solo muro.
- `RealCatalogSmokeTests`: sigue pasando contra el catálogo real (los nuevos flags por
  keyword se validan en es_MX/en_US).

## Fuera de alcance (v3)

- Bajar `champion/{Key}.json` (spells/pasivas) y parsear su texto: poco confiable; el kit
  se sigue modelando con listas curadas.
- Winrates/meta externos, runas, hechizos, orden de habilidades, overlay in-game.
- Memoria entre ticks / histéresis explícita: las rampas difusas ya reducen el temblor;
  la histéresis con estado previo queda para una iteración futura si hiciera falta.

## Resultado de la ejecución (2026-07-03)

Implementado por TDD sobre la base verde de 144 tests → **174 tests, todos verdes**
(incluye la prueba de humo contra el catálogo REAL en_US 16.13.1 cacheado en la máquina).

Piezas entregadas:

- `Fuzzy` (nuevo, público): `Clamp01`, `Ramp` (creciente/decreciente/escalón), `Triangle`,
  `And`/`Or`/`Not`/`AndProduct`. 14 tests de monotonía, saturación y combinadores.
- `StaticItem.ReducesCritDamage` + `ItemsConfig.CritReductionKeywords` (detección por
  keyword: "from critical strikes" / "de los golpes críticos"; valida contra el Randuin's
  real y NO marca Filo Infinito). Confirmado sobre datos reales.
- `TeamThreat` + `ThreatAnalyzer`: 11 grados difusos (`PhysicalSkew`, `MagicalSkew`,
  `ArmorStack`, `MrStack`, `Sustain` map-aware, `Burst`, `CritThreat`, `PercentHpTrue`,
  `HardEngage`, `EnemyAntiHeal`, `EnemyTankiness`) + listas de kit
  `PercentHpTrueDamageChampions` / `HardEngageChampions`. 8 tests nuevos.
- `ItemAdvisor.ScoreItem` v3: base × fit + contrapartidas difusas
  (`core + offense + defense − devaluación-lifesteal`), nuevos counters (anti-crit,
  anti-tanque, supervivencia vs. enganche, atenuación %HP/verdadero) y devaluación del
  robo de vida si el enemigo ya cortó tu curación. Umbrales v2 preservados como cruces
  μ≈0.5. 7 tests nuevos (prioridad, anti-crit, devaluación lifesteal, monotonía sin
  acantilado, anti-tanque, %HP damp, categoría).
- `ItemRecommendation` gana `Priority` (fracción del top) y `Category`
  (`Core`/`Counter`/`Defense`/`Spike`); la UI muestra la insignia
  "CATEGORÍA · N% match" en cada tarjeta de item.

Calibración: `BootsFor` se dejó con umbrales duros a propósito (elección discreta). Las
magnitudes viven como constantes en `ItemAdvisor`; los grados en `ThreatAnalyzer`. Ningún
comportamiento de selección de la v2 se rompió.
