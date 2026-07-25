# データ・素材の出典

このプロジェクトで使用している外部データと、その利用条件をまとめる。
表記が義務づけられているものについては、該当シーンの画面上にも表示している。

## 雲（planetReal シーン）

near real-time の雲マップを、起動時に取得して地球に貼っている。
配信元は [live-cloud-maps](https://github.com/matteason/live-cloud-maps)（Matt Eason, CC0 1.0）。
元となる雲の観測データは EUMETSAT による。

**表記が必須：**

> Contains modified EUMETSAT data

[EUMETSAT のデータ利用条件](https://www.eumetsat.int/eumetsat-data-licensing)に基づく。
`PlanetRealGlobe` の `showAttribution` により、planetReal シーンの画面左下にこの表記を出している。
この表示を消す場合は、作品のクレジット等 別の場所で必ず表記すること。

取得先 URL: `https://clouds.matteason.co.uk/images/4096x2048/clouds-alpha.png`
（3時間ごとに更新。通信できない場合は、手続き生成した雲にそのまま留まる）

## 地表（planetReal シーン）

NASA Blue Marble Next Generation（2004年1月、地形・海底地形入り）。
[Visible Earth](https://visibleearth.nasa.gov/collection/1484/blue-marble) より。

米国政府の著作物のためパブリックドメインで、表記の義務はない。
本プロジェクトでは元の 21600×10800 を 4096×2048 に縮小して使用している
（`Assets/Textures/earth_bluemarble_200401_4k.png`）。

## 地球モデル

`Assets/Models/EARTH.fbx`（Surface / Clouds / Atom の三層構造）。
外部から提供されたモデル。配布元の利用条件は各自で確認のこと。

## 音声

`Assets/Audio/monden_voice_huhuhu` / `morita_voice_uri` は本人による録音。

m4a は Unity がオーディオとして扱えないため、同じ内容の wav を併置し、そちらを参照している。
