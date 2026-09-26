# Spike 005 — 把 2D 骨架演出搬去 Godot 4 / Unity

**問題**：spike 004 做嘅骨架拼貼演出（web/CSS 版），搬去真正遊戲引擎要重做幾多？

**答案（實測，非估算）**：**素材同數值 100% 重用，代碼要重寫但只係幾十行對應關係。**

## 唯一輸入

`../004-2d-rig/rig-data.json` — 舞台尺寸、4 個部件嘅裁切框、4 個樞軸。
**呢個檔就係便攜層**：兩個引擎都係「讀同一個 JSON → 砌同一個骨架 → 用同一組 keyframes」。

## 對應表（三個實作，同一套概念）

| 概念 | Web (spike 004) | Godot 4 | Unity |
|---|---|---|---|
| 樞軸（繞關節轉） | CSS `transform-origin: OX OY` | `Node2D` 樞軸節點 + `rotation` | 父 `GameObject` + `localEulerAngles.z` |
| 部件位置 | `<img style="left/top">` | 子 `Sprite2D`（`centered=false`，position 補償） | 子 `SpriteRenderer`（localPosition 補償中心） |
| 疊放次序 | `z-index` | 節點在樹中次序 | `sortingOrder` |
| 動畫 | `@keyframes` + `cubic-bezier` | `AnimationPlayer` + `Animation`（rotation track） | `AnimationClip` 或 `Update()` 插值 |
| 根節點前撲 | `translateX` | root `Node2D` 嘅 `position` track | root Transform 本地位置 |

## 驗證（可重跑）

```bash
cd godot
GODOT=<godot 執行檔>
"$GODOT" --headless --path . -- --verify          # 幾何 + 動畫抽樣
xvfb-run -a "$GODOT" --path . --rendering-driver opengl3 -- --shot   # 真 render 出 PNG
```

**結果（Godot 4.7.2-stable，2026-09-26 實跑）**

| 驗證項 | 結果 |
|---|---|
| 靜止姿勢：4 個部件嘅世界座標 + 尺寸 vs `rig-data.json` | **4/4 全部一致**（誤差 < 0.01px） |
| 動畫抽樣：6 個時間點 × 4 條 track（臂／軀幹／頭／根） | **6/6 全部落在 0.6° 內**（例：t=0.162s 臂 −27.0° 要 −27°） |
| 部件圖匯入（WebP） | 4/4 載入成功，尺寸同原圖一致 |
| 真 render 出畫面（xvfb + OpenGL3） | 5 格 PNG 全部出到，肉眼核對：人形完整、無斷肢、手臂有動 |

實測輸出：
```
[OK] arm_front  世界座標 (213.0, 0.0)（要 (213.0, 0.0)）｜尺寸 (252.0, 211.0)（要 (252.0, 211.0)）
[OK] t=0.323s  臂 15.0（要 15）｜軀幹 7.0（要 7）｜頭 3.0（要 3）｜根 x 30.0（要 30）
=== 結果：全部通過（失敗 0 項）===
```

## 各引擎注意事項

**Godot 4**
- WebP **原生支援**（`load()` 直接用，實測通過）
- 兩條路：(a) 本 spike 做法：樞軸 `Node2D` + `Sprite2D`（最簡單、最快）
  (b) `Skeleton2D` + `Bone2D`：可以做骨骼權重／IK，但設定複雜得多，唔係必要
- 官方文檔：2D skeletons／Cutout animation

**Unity**
- **WebP 唔支援匯入**（官方支援格式無 WebP，社群一直有 feature request）→ 已順手 export PNG 版（`Assets/RigData/Textures/*.png`）
- 兩條路：(a) 本 spike 做法：父 GameObject（樞軸）+ 子 SpriteRenderer（唔需要裝 2D Animation package）
  (b) `2D Animation` package 嘅 Sprite Skin + bones（要 PSD/PSB 或者自己分 mesh）
- **座標要翻**：`rig-data.json` 用螢幕座標（Y 向下），Unity 2D 係 Y 向上 → `y_unity = stageH − y_screen`，而且旋轉方向要反號（`z = −angle`）。呢個係最容易出錯嘅一點。

## 現實結論

| 項目 | 結果 |
|---|---|
| 重用比例 | 素材 100%（PNG/WebP 兩版都有）｜數值 100%（同一個 JSON）｜代碼 0%，但對應關係只有 5 行表 |
| 搬去 Godot 4 實際時間 | 一個 session（包括裝 engine、寫 port、除錯、驗證到 render 出圖） |
| 一次性成本 | 之後加招／加角色只需改 JSON 同 keyframes，兩個引擎都唔使重做素材 |
| 未驗證嘅部分 | **Unity 版未編譯／未執行過**（本機無 Unity：需帳號＋license＋體積）—— 數學同 Godot 版一樣，但第一次開預期要微調 |

## 檔案

- `godot/` — 可開嘅 Godot 4.7 專案（`rig.gd` 讀 JSON 砌骨架，含 `--verify` / `--shot` 兩個模式）
- `godot/shots/pose_*.png` — 真 render 出嘅逐格圖（`godot-shots.png` 係拼接版）
- `unity/Assets/Scripts/RigBuilder.cs` — Unity 版（同一個 JSON；未驗證）
- `unity/Assets/RigData/` — PNG 部件 + Unity 友善格式嘅 JSON