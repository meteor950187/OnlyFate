# GeoNames 城市參考資料

來源為 [GeoNames](https://www.geonames.org/)，採 [CC BY 4.0](https://creativecommons.org/licenses/by/4.0/)。允許商用及改作，需署名；資料原樣提供，無精確度保證。原始欄位與收錄範圍見 [官方資料說明](https://download.geonames.org/export/dump/readme.txt) 及此目錄的 readme.txt。

- cities15000.zip：城市代表點、別名、人口、經緯度及 IANA 時區；不是每個鄉鎮都收錄。
- countryInfo.txt：國家 ISO 代碼及名稱。
- admin1CodesASCII.txt：行政區名稱，協助辨識同名城市。
- source.json：2026-11-02 下載來源、檔案 SHA-256、授權及精度。更新時整組重新核對，不覆寫既有 run 的來源紀錄。

資料檔未改寫；程式載入時做名稱正規化／別名查找，並自行增加少數中文國家別名。每次選用的座標與來源版本會保存於 Step 2 InputJson；不以城市座標宣稱定位醫院。應用程式內顯示來源與授權，建置隨附此說明及來源收據。
