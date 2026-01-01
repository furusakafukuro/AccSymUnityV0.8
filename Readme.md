---
title: "Relativistic Particle Demo (Unity) — README"
date: 2026-01-01
tags:
  - unity
  - vr
  - quest3
  - relativistic
  - boris
  - particle-simulation
  - elektromagnetism
aliases:
  - Relativistic Boris Unity Demo
---

# Relativistic Particle Demo (Unity) — README

## 概要
Unity上で荷電粒子運動をリアルタイムに可視化する最小実装。  
表示時間（tAnim）と物理時間（tPhys）を分離し、Quest等で計算が追いつかない場合でも破綻しにくい構造になっている。

主な特徴
- **相対論Boris法（u=γv を保持）**で安定に積分（\(|v|\to c\) でも破綻しにくい）
- **CPU予算（budgetMs）**と **粒子間引き（maxActivePerFrame）**でフレーム維持
- **LOD（距離別更新間隔）**と **表示外挿（visual-only）**で描画負荷を制御
- HUDで `Due/Active/Int/Miss`、`Requested/Effective`、予算・ステップを監視

> [!NOTE]
> 本実装は設計・検証・教育用途のミニマル実装。厳密な境界条件、空間電荷、衝突、散乱などは含まない。

---

## 使い方

### スクリプト配置
`Assets/Scripts/Relativistic/` を作り、以下を配置（ファイル名＝クラス名）。

必須
- `ParticleState.cs`
- `IFieldProvider.cs`
- `ParticleIntegratorRelativisticBorisU.cs`
- `SimulationController.cs`
- `StatusHUD.cs`

例の場（任意）
- `FieldProviderSimpleRF.cs`

> [!WARNING]
> 同名クラスが既にあるとコンパイル衝突する。衝突する場合は namespace を切るか、既存を削除/改名。

---

### シーン構成（最小）
1. Hierarchyで空オブジェクト `SimRoot` 作成  
2. `SimRoot` に `SimulationController` を Add Component  
3. `SimRoot` に `FieldProviderSimpleRF` を Add Component  
4. `SimulationController > Field Provider Behaviour` に **FieldProviderが付いたオブジェクト**（通常 `SimRoot`）を割り当てる  

---

### 粒子の表示（particleVisuals）
`SimulationController` は `particleVisuals[]` に Transform を割り当てないと「計算はしても見えない」。

見え方を合わせる（重要）
- デフォルトSphereは直径1 unit（=1m相当）なので、移動がcm/sでも“止まって見える”
- Sphere の Scale を `0.01`（直径1cm）程度に下げるのが有効

> [!TIP]
> PC検証時は `maxActivePerFrame = particleCount` にして `Miss=0` に近づけると挙動が追いやすい。

---

### HUD表示（UGUI Text）
1. `UI > Canvas` を作成  
2. Canvas子に `UI > Text` を作成（TextMeshProではなくUGUI Text）  
3. TextのRectTransform Heightを十分大きく（例：300）  
4. Textの `Vertical Overflow = Overflow`  
5. Textに `StatusHUD` を Add Component  
6. `StatusHUD.text` にそのTextを割り当て  
7. `SimulationController.statusHUD` にその `StatusHUD` を割り当て  

---

## パラメタの説明

### SimulationController

#### 時間・速度
- `basePhysPerAnimSecond`（s/s）  
  表示1秒あたりに進めたい物理時間。例：
  - `1e-9` → 1 ns/s
  - `1e-8` → 10 ns/s
  - `1e-7` → 100 ns/s
- `fastFwd`  
  物理時間進行倍率（2倍刻み推奨）。例：1,2,4,8…
- `dtAnimOverride`  
  0より大きい場合、Time.deltaTimeの代わりに固定表示dt（デバッグ用）。

#### 計算予算・間引き
- `budgetMs`（ms）  
  1フレームで物理計算に使う時間予算。Quest3なら 1–3ms目安。
- `particleCount`  
  粒子数。
- `maxActivePerFrame`  
  1フレームで実際に積分する最大粒子数。下げるほど軽いが `Miss` が増える。

#### dt制御（安定性）
- `maxStepsPerFrame`  
  1フレーム内の最大サブステップ数。
- `epsB`  
  Boris安定条件（目安）：$\omega_c \Delta t \le \varepsilon_B$
- `epsX`  
  空間移動条件（目安）：$|v|\Delta t \le \varepsilon_X \, dx$
- `dx`（m）  
  場サンプリングの特徴長（セルサイズ、ギャップ等）。
- `bMaxFallbackTesla`（T）  
  FieldProviderがB最大値を返さない場合の保守値。dtが過大になって発散するのを防ぐ。

#### LOD（更新頻度）
- `nearDist, midDist`（m）  
  カメラ距離の閾値。
- `nearInterval, midInterval, farInterval`（frames）  
  更新間隔。far=4なら4フレームに1回の期限。

#### 相対論
- `speedOfLight`（m/s）  
  $c = 299792458\ \mathrm{m/s}$。

#### 表示外挿（visual-only）
- `extrapolateFactor`  
  外挿上限：$\Delta t_{\mathrm{clamp}} = \mathrm{interval}\cdot \Delta t_{\mathrm{phys,done}}\cdot \mathrm{factor}$
- `maxExtrapolatePhysSec`  
  外挿の絶対上限（物理秒）。

---

### FieldProviderSimpleRF（例）
- `EStatic`（V/m）, `BStatic`（T）  
  一様静電場・一様磁場
- `enableRF`  
  RFを有効化
- `rfFreqHz`（Hz）  
  例：$2.856\times10^9$, $3.52\times10^8$
- `rfE0`（V/m）  
  RF電場振幅
- `kDir`  
  伝搬方向
- `eDir`  
  偏波方向
- `BMaxEstimateTesla`（T）  
  dt制御用の最大磁場。**必ず設定推奨**。

---

## 物理的バックグランド（LaTeX）

### 時間スケール分離（tAnim と tPhys）
要求する物理進行：
$$
\Delta t_{\mathrm{phys,req}} = \Delta t_{\mathrm{anim}} \cdot s_{\mathrm{req}}
$$

ここで $s_{\mathrm{req}}$ は `basePhysPerAnimSecond * fastFwd`（単位：物理秒/表示秒）。  
実際に進められた物理進行を $\Delta t_{\mathrm{phys,done}}$ とすると、実効速度は
$$
s_{\mathrm{eff}} = \frac{\Delta t_{\mathrm{phys,done}}}{\Delta t_{\mathrm{anim}}}
$$

計算予算や安定条件により $s_{\mathrm{eff}} < s_{\mathrm{req}}$ となる。

---

### 相対論ローレンツ力と状態変数
ローレンツ力：
$$
\frac{d\mathbf{p}}{dt} = q\left(\mathbf{E} + \mathbf{v}\times \mathbf{B}\right)
$$

相対論運動量：
$$
\mathbf{p} = \gamma m \mathbf{v},\qquad
\gamma = \frac{1}{\sqrt{1-\frac{|\mathbf{v}|^2}{c^2}}}
$$

本実装の状態変数：
$$
\mathbf{u} \equiv \frac{\mathbf{p}}{m} = \gamma \mathbf{v}
$$

変換：
$$
\gamma = \sqrt{1 + \frac{|\mathbf{u}|^2}{c^2}},\qquad
\mathbf{v} = \frac{\mathbf{u}}{\gamma}
$$

---

### 相対論Boris法（u保持）
1) 電場の半ステップ：
$$
\mathbf{u}^- = \mathbf{u}^n + \frac{q}{m}\mathbf{E}\frac{\Delta t}{2}
$$

2) $\gamma^-$：
$$
\gamma^- = \sqrt{1+\frac{|\mathbf{u}^-|^2}{c^2}}
$$

3) 磁場回転：
$$
\mathbf{t} = \frac{q}{m}\frac{\mathbf{B}\Delta t}{2\gamma^-},\qquad
\mathbf{s} = \frac{2\mathbf{t}}{1+|\mathbf{t}|^2}
$$

$$
\mathbf{u}' = \mathbf{u}^- + \mathbf{u}^- \times \mathbf{t}
$$
$$
\mathbf{u}^+ = \mathbf{u}^- + \mathbf{u}' \times \mathbf{s}
$$

4) 電場の半ステップ：
$$
\mathbf{u}^{n+1} = \mathbf{u}^+ + \frac{q}{m}\mathbf{E}\frac{\Delta t}{2}
$$

5) 速度・位置更新：
$$
\gamma^{n+1}=\sqrt{1+\frac{|\mathbf{u}^{n+1}|^2}{c^2}},\qquad
\mathbf{v}^{n+1}=\frac{\mathbf{u}^{n+1}}{\gamma^{n+1}}
$$

$$
\mathbf{x}^{n+1}=\mathbf{x}^{n}+\mathbf{v}^{n+1}\Delta t
$$

---

### dt制御（保守的上限）
- サイクロトロン周波数（保守的に $\gamma=1$）：
$$
\omega_c^{\max} = \frac{|q|B_{\max}}{m},\qquad
\Delta t \le \frac{\varepsilon_B}{\omega_c^{\max}}
$$

- 空間移動制限：
$$
\Delta t \le \frac{\varepsilon_X\,dx}{v_{\max}}
$$

---

## プログラム構成
- `SimulationController.cs`：dt決定／due判定／active選択／積分／HUD更新／描画外挿  
- `ParticleIntegratorRelativisticBorisU.cs`：相対論Boris（u保持）＋ $\mathbf{u}\leftrightarrow\mathbf{v}$ 変換  
- `ParticleState.cs`：状態（$\mathbf{x}$, $\mathbf{u}$, $q$, $m$）  
- `IFieldProvider.cs`：$\mathbf{E}(\mathbf{x},t)$, $\mathbf{B}(\mathbf{x},t)$, $B_{\max}$ 提供  
- `FieldProviderSimpleRF.cs`：例：静的場＋簡易RF  
- `StatusHUD.cs`：状態表示  

---

## Quest 3 推奨プリセット（60fps / 300 particles）

### プリセットA（安定・軽い）
- `particleCount = 300`
- `budgetMs = 2.0`
- `maxActivePerFrame = 120`
- `maxStepsPerFrame = 16`
- `basePhysPerAnimSecond = 1e-8`（10 ns/s）
- `fastFwd = 1`
- `dx = 0.005`
- `epsB = 0.1`
- `epsX = 0.1`
- `extrapolateFactor = 50`
- Sphere scale：`0.01`

### プリセットB（見た目優先：Miss減）
- `maxActivePerFrame = 200`
- `budgetMs = 2.5`

### プリセットC（PC検証：全員積分）
- `maxActivePerFrame = particleCount`
- `budgetMs = 5〜10`
- `extrapolateFactor = 1`

---

## 検証用の物理テスト

### テスト1：E=0, 一様B（エネルギー保存）
設定
- $\mathbf{E}=\mathbf{0}$
- $\mathbf{B}=(0,0,B_0)$
- 初期速度 $\mathbf{v}\perp \mathbf{B}$

期待
- $|\mathbf{u}|$ がほぼ一定
- 円運動
$$
\omega = \frac{|q|B_0}{\gamma m},\qquad
r = \frac{\gamma m v_\perp}{|q|B_0}
$$

### テスト2：B=0, 一様E（直線加速）
設定
- $\mathbf{B}=\mathbf{0}$
- $\mathbf{E}=(0,0,E_0)$

期待
- z方向加速
- $\gamma$ が増えて加速が鈍る

### テスト3：E ⟂ B のドリフト（非相対論チェック）
期待（非相対論近似）：
$$
\mathbf{v}_d = \frac{\mathbf{E}\times\mathbf{B}}{|\mathbf{B}|^2}
$$

---

## トラブルシュート（短縮版）
- `StepsPerformed=0` → 予算不足（budgetMs↑ / maxActive↓ / particleCount↓）
- `Miss` が大きい → 間引き過大（maxActive↑ または particleCount↓）
- 動いているが塊に見える → Miss多＋外挿クランプ小（extrapolateFactor↑、または active↑）
- E/Bを入れても動かない → fieldProvider未割当、Eが実は0、初期速度0、particleVisuals未割当を確認

---