# LoL Advisor — Pasada crítica de rendimiento (diseño)

**Fecha:** 2026-07-02
**Estado:** Ejecutado de forma autónoma bajo directiva `/goal` ("haz mejoras y sé
crítico para que esta sea una herramienta demasiado buena que aumente mi rendimiento").

## Auditoría crítica (qué falla hoy para el jugador)

1. **El minuto 0 es inútil.** Con el oro inicial de ARAM la app ofrece items de 2,200+
   ("need 2,200 more gold"): lo accionable es la **compra inicial** (Guardian's
   Orb/Horn/Blade/Hammer o Doran's), que nunca se recomienda porque el catálogo de
   "items completos" las excluye por precio mínimo.
2. **ARAM solo deja comprar muerto** y la app no conecta ese momento con el oro
   disponible: el instante exacto en que el consejo es accionable pasa en silencio.
3. **Razones genéricas.** "core for your support build" ×3 no aporta nada: el jugador
   no sabe *por qué* ese item pega con su build.

## Mejoras (data-driven, sin listas curadas)

### A. Compra inicial (ARAM)

- Descubrimiento: en ddragon los starters llevan el tag **`Lane`**. En el mapa 12 el
  pool real es exactamente Doran's ×5 + Guardian's ×4 (sin junglas ni items de quest).
- Catálogo: `AramStarterItems` = `Lane` + purchasable + mapa 12 − `GoldPer` − `Jungle`,
  orden por precio desc.
- `ItemAdvisor`: si es ARAM, `GameTime <= StarterWindowSeconds` (config, 90 s) y no
  llevas ningún item real (ni consumible ni trinket), el plan trae
  `Starter = StarterAdvice(item, razón)`: el starter con mejor fit de tags para tu
  arquetipo (empates → el más caro, que en ARAM es el Guardian's correcto).
- UI: línea verde "Start: Guardian's Orb (950) — opening buy for your mage build".
- Solo ARAM en v1: en la Grieta el starter depende del rol/línea que la API no expone,
  y ddragon miente con los mapas de los Guardian's (dice mapa 11 y no se venden ahí).

### B. Aviso de tienda abierta (ARAM, muerto)

- En ARAM la tienda solo abre muerto. Si `me.IsDead` y hay recomendaciones:
  - alcanza para terminar el top → `ShopAlert`: "Shop open while dead — finish X now (3,400)."
  - si no, hay componente comprable → "Shop open while dead — buy Y (1,300) toward X."
- UI: línea ámbar en negrita arriba del panel (mismo tono que el aviso de augments).

### C. Razones específicas de fit

- El fallback "core for your X build" se reemplaza por los 2 tags que más pesan para
  tu arquetipo con nombres legibles: "fits your mage build: ability power + ability
  haste". Mapa tag→label en código (`TagLabel`); si el item no tiene tags con peso,
  se conserva el texto genérico.

## Piezas

- `IStaticData.AramStarterItems` + implementación en `DataDragonCatalog` (+ Empty).
- `ItemsConfig.StarterWindowSeconds = 90`.
- `ItemAdvicePlan` gana `StarterAdvice? Starter` y `string? ShopAlert`.
- `MainViewModel.StarterLine` / `ShopAlertLine` + dos TextBlocks en la tarjeta.
- Tests: starter por arquetipo (maga→Orb, tiradora→Hammer), supresión del starter
  (con item comprado / fuera de ventana / en Grieta), ShopAlert (muerto+oro → finish;
  vivo → null; CLASSIC → null), razones de fit específicas.
