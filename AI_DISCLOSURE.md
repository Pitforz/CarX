# AI-контент в проекте CarX

Требование хакатона: всё, что сделано с помощью ИИ, записываем сюда.

| Что (файл/папка) | Тип (арт/текст/музыка/голос/код) | Инструмент | Кто добавил | Дата |
|---|---|---|---|---|
| .gitignore, .gitattributes, README.md, структура папок | конфиг/текст | Claude | BROFedka | 2026-11-12 |
| Assets/_Project/Scripts/Editor (CarXSandboxBuilder, TunnelKit), модули тоннеля TunnelKit | код/3D-меши | Claude | BROFedka | 2026-11-12 |
| Assets/_Project/Scripts/Level, Scripts/Gameplay/Flashlight.cs, Scripts/Editor (TunnelAssets, TunnelPathEditor, TunnelDressing, TunnelMazeBuilder, CarXRemoteBridge) — генератор тоннелей, подъёмов, лабиринта | код | Claude | BROFedka | 2026-11-12 |
| Assets/_Project/Shaders (LightShaft, LightRay, DustMote, Emissive, CarXShaft.hlsl) | код (шейдеры) | Claude | BROFedka | 2026-11-12 |
| Assets/_Project/Textures/Tunnel, Materials/Tunnel — процедурные текстуры и материалы (генерирует код Claude) | арт | Claude | BROFedka | 2026-11-12 |
| Assets/_Project/Scenes/TunnelMaze_Demo, NPC_Demo; Scripts/Editor/NpcDemo; Prefabs/NpcDemo | сцены/код | Claude | BROFedka | 2026-11-12 |
| Граф тоннелей: Scripts/Level (TunnelNetwork, TunnelType, TunnelTypeLibrary), Scripts/Editor (TunnelNetworkEditor, TunnelGraphAssets, TunnelGraphDemo), Tunnels/*.asset, Scenes/TunnelGraph_Demo | код/сцена | Claude | BROFedka | 2026-11-13 |
| Комнаты и колонны (Scripts/Level/TunnelRoom, TunnelColumn), выгрузка прототипов (Scripts/Editor/TunnelPrototypeExport), модели Tunnels/Prototypes/*.obj | код/модели | Claude | BROFedka | 2026-11-13 |
| Стыки тоннелей разных типов, срез стен комнат, якоря для пропсов/NPC/граффити: Scripts/Level (TunnelAnchor, TunnelNetwork, TunnelPath, TunnelRoom, TunnelSection, MeshKit), Scripts/Editor (TunnelAnchorEditor, TunnelNetworkEditor, TunnelGraphDemo) | код | Claude | BROFedka | 2026-11-13 |
| Швы между плитами (одинаковые 6 мм, пол одной плоскостью), стыковка врезок на уклоне и повороте, текстура пола в мировых координатах: Scripts/Level (TunnelPath, TunnelNetwork, TunnelType, TunnelRoom, TunnelJunction), Scripts/Editor (TunnelAssets, TunnelGraphDemo), Tunnels/Types/*.asset, перегенерированные текстуры Textures/Tunnel | код/арт | Claude | BROFedka | 2026-11-14 |
| README.md (инструкция для новичков) | текст | Claude | BROFedka | 2026-11-12 |
| PlayerCapsule, контроллер от первого лица, Assets/_Project/Scripts/Gameplay и Assets/_Project/Shaders/MuzzleFlash.shader — управление игроком, фонарик, оружие, инвентарь и меню | код/настройки | Codex | Дмитрий | 2026-11-14 |
| Assets/_Project/Textures/UI/InventoryWheel — колесо инвентаря и иконки | арт | Codex | Дмитрий | 2026-11-14 |
| Assets/_Project/Audio/Flashlight — обрезка предоставленной записи щелчка | обработка звука | Codex | Дмитрий | 2026-11-14 |
| Дневной свет тоннелей: общие настройки для выходов, решёток и люков, адаптация глаза, теневая оболочка против просветов, текстура стен в метрах, ровные стыки у пола, перегенерированный бетон без трещин и точек: Scripts/Level (TunnelEyeAdaptation, TunnelTypeLibrary, TunnelShaftBuilder, ShaftSpec, TunnelExit, TunnelNetwork, TunnelPath), Scripts/Editor (TunnelAssets, TunnelAssets.Culvert), Textures/Tunnel | код/арт | Claude | BROFedka | 2026-11-15 |
