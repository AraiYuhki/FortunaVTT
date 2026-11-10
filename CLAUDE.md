# CLAUDE.md

## プロジェクト概要
FortunaVTT は、秘匿を重視するサーバー権威型のブラウザ製TRPG用VTT（MIT）。
どどんとふ（`D:\Projects\DodontoF`）の後継的な位置づけ。

## 設計の大原則
1. **サーバー権威**: 状態の正はサーバー。クライアントは操作要求を送るだけ。
2. **秘匿**: 見えないデータはクライアントに送らない。可視性判定は `Fortuna.Domain` の Visibility 層に集約し、他で判定を重複実装しない。
3. **差分配信**: ルーム全体の再送ではなく、受信者ごとに絞った差分を SignalR で配信する。
4. **スコープ厳守**: マイルストーンは「友人と1回遊べる」単位。M1 = 秘匿チャット + コマ移動 + ダイス。それ以外は M1 完了後。

5. **GM に特権なし**: GM は固定ロールではない。誰も他人の非公開データを見られない（ADR-003）。
6. **素材の権利**: DodontoF 由来の画像・音声・カードはリポジトリにコミットしない。取り込みツール経由で `data/templates/`（gitignore 済み）に置く（ADR-004）。

## 構成
- `src/Fortuna.Server` — ASP.NET Core (net10.0) + SignalR
- `src/Fortuna.Domain` — ドメイン層
- `tests/Fortuna.Tests` — xUnit
- `client` — TypeScript + Vite（マップ描画は PixiJS 予定）
- `docs/adr` — 設計判断。大きな設計変更の前に ADR を書く。

## コマンド
```bash
dotnet build FortunaVTT.slnx
dotnet test FortunaVTT.slnx
cd client && npm run dev
```

## ダイス
BCDice は C# 移植版（`D:\Projects\DiceBotTranslate` → `AraiYuhki/BCDiceForCSharp`、移植は進行中）を使う。
未移植システムは当面 `bcdice-api` で補う想定。

## 参考資料
- 旧 FortunaClient / `TRPG/Fortuna` は Photon 利用で同期が複雑化した前例。同じ轍を踏まない。
- `Miskatonic-Table` に可視性モデル（`Event.IsVisibleTo`）の先行実装あり。
