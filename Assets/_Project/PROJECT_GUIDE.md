## 這份專案要解決什麼

你們的遊戲是 **3D 世界的 2D 玩法（FEZ 類）**：世界是 3D 場景，但玩家在任一時刻通常只能在一個 2D 平面上移動；透過鏡頭旋轉（常見 90 度）切換可走平面，並以互動/敘事/解謎為主、少量動作作為節奏點。

這份指引的目標是讓「幾乎零程式」的團隊也能持續產出內容，並且不會因為功能變多而整個專案難以維護。

## 核心原則（請全員遵守）

- **資料驅動**：關卡、互動、對話、任務盡量用 `ScriptableObject`（或 JSON）描述，程式只做「讀資料→執行」。
- **弱耦合**：系統之間以事件/訊號溝通，避免互相直接抓彼此的 Component。
- **垂直切片優先**：先做 10–15 分鐘完整可玩流程；之後只「加內容」與「補少量能力」，不要一次做滿所有系統。

## 資料夾約定（Assets/_Project）

- `Art/`：你們自己產出的美術（請按子資料夾分類：Characters/Environment/UI/FX）
- `Audio/`：音效/音樂/語音
- `Data/`：**所有 ScriptableObject 資料**（對話、任務、互動設定、能力設定、關卡設定）
- `Prefabs/`：Prefab（互動物件、UI、敵人/道具）
- `Scenes/`：正式場景（測試請放 `Testing/`）
- `Scripts/`：程式碼（`Core/` 放共用基礎，`Gameplay/` 放各系統）
- `UI/`：UI 資源（Sprites、UI Prefabs、UI 動畫）
- `Testing/`：測試場景與測試資料（可隨時刪/重建）

## 命名規範（簡化版）

- **檔名**：PascalCase（例：`DialogueGraph.asset`、`QuestDefinition.asset`、`InteractableDoor.prefab`）
- **一個檔案一個主體**：`Foo.cs` 主要就放 `class Foo`
- **資料（ScriptableObject）**：結尾用 `Definition` / `Config`（例：`QuestDefinition`、`CameraConfig`）
- **Prefab**：以用途命名（例：`UI_DialogueCanvas`、`Interactable_Lever`）

## 團隊協作最低限度

- 場景不要多人同時改同一個（場景衝突會很痛）。
- 內容盡量在 `Data/` 與 `Prefabs/` 產出，讓整合成本下降。

## FEZ 式深度／四向視角（簡要）

- **深度軸**預設來自 **`FezWorldViewState`**（依樞紐 Y 角對應 ±X／±Z 四向），**不要用 MainCamera.forward** 當深度，避免 Cinemachine 插值造成錯位。
- 在與 **`CameraRotationAbility.targetPivot` 相同**的物件（或其父／子）上掛 **`FezWorldViewState`**，並將 **View Pivot** 指到該旋轉樞紐；旋轉結束時技能會呼叫 `RefreshFromPivot()`。
- 若舊場景的 `FezDepthAxisSource` 仍要沿用鏡頭，請在 Inspector 改回 **MainCamera**（枚舉順序已調整，舊存檔可能需重選一次）。
- **單向平台**：在 DepthProxy 上加 **`FezOneWayPlatform`**；可勾選 **Auto Include Parent Collider**（陣列留空）讓**父物件平台本體**與透明箱一併暫關，或手動填 **Platform Body Colliders**。頂面高度可選 **Top Surface Reference**（不填則優先用本體 Collider）。

