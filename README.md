# Metlab AVA

MetraZero 制作の VRChat アバター向けツールです。

## 収録ツール

| ツール | パッケージID | 内容 | バージョン |
| --- | --- | --- | --- |
| [トグルをつくるやつ](Packages/com.metlab.ava.toggle/README.md) | `com.metlab.ava.toggle` | 衣装やアクセサリのON/OFFメニューを Modular Avatar で非破壊に作るエディタ拡張。 | 1.0.0 |
| [房を選んで髪色をかえるやつ](Packages/com.metlab.ava.haircolor/README.md) | `com.metlab.ava.haircolor` | シーンビューで髪の房をクリックして、房ごとに色を塗り分けるエディタ拡張。 | 1.0.0 |
| [アセットのショートカットをつくるやつ](Packages/com.metlab.shortcut/README.md) | `com.metlab.shortcut` | よく使うフォルダやアセットのショートカットを、実際のフォルダ構成とは別に整理できるタブ。フォルダの色分け付き。 | 1.1.0 |

## 導入方法（VCC / ALCOM）

1. 次の URL を VCC（または ALCOM）の「Packages > Add Repository」に登録する

   `https://metrazero.github.io/vpm-repo/index.json`

   または [こちらのページ](https://metrazero.github.io/vpm-repo/) の「VCC に追加」ボタンから登録できます。

2. プロジェクトの「Manage Project」から、使いたいツールを追加する

アバター向け（Metlab AVA）とワールド向け（Metlab WLD）は同じ URL で配信しています。
ワールド向けのツールはワールド用 SDK が必要なので、アバター用プロジェクトには入りません（逆も同じ）。

## メニュー

ウィンドウを持つツールは、すべて `Tools > Metlab` の下にあります。

## ライセンス

[MIT License](LICENSE)
