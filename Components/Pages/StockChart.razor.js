// StockChart.razor.js

export function renderStockChart(containerId, stockLabel, data) {
    const chartDom = document.getElementById(containerId);
    if (!chartDom) return;

    // ECharts 由 App.razor 的 <script> 全域載入；重複繪製時沿用同一個圖表實例
    const myChart = echarts.getInstanceByDom(chartDom) || echarts.init(chartDom);

    const dates = data.map(item => item.date);

    // 注意：C# 傳過來的屬性開頭通常會變成小寫 (camelCase)
    const kValues = data.map(item => [item.open, item.close, item.low, item.high]);

    const option = {
        title: {
            text: `${stockLabel} 技術分析圖`,
            textStyle: { color: '#fff' }
        },
        // 調整圖表內邊距，留位置給下方的縮放滑桿與標籤
        grid: {
            left: '8%',
            right: '8%',
            bottom: '20%',
            top: '12%'
        },

        // 🛠️ 1. 顯示滑鼠游標所指當天的開盤、收盤、最高、最低 (Tooltip)
        tooltip: {
            trigger: 'axis',          // 觸發類型：座標軸觸發（滑鼠指到該點垂直線上即觸發）
            axisPointer: {
                type: 'cross'         // 十字準線指示器，十字交點會對齊游標
            },
            backgroundColor: 'rgba(30, 34, 45, 0.9)', // 深色背景符合你的面板風格
            borderColor: '#434651',
            borderWidth: 1,
            textStyle: {
                color: '#d1d4dc'
            },
            // 自訂提示框內容，精準顯示 K 線的開高低收
            formatter: function (params) {
                // params[0] 代表第一個 series (也就是我們的 Candlestick K線)
                const param = params[0];
                if (!param) return '';

                // ECharts Candlestick 的 data 結構：[開盤, 收盤, 最低, 最高]
                const date = param.name;
                const open = param.data[1];
                const close = param.data[2];
                const low = param.data[3];
                const high = param.data[4];

                // 根據漲跌決定文字顏色 (漲紅跌綠)
                const changeColor = close >= open ? '#ef5350' : '#26a69a';

                return `
                <div style="font-weight:bold; margin-bottom:5px; color:#fff;">日期：${date}</div>
                <div style="color:${changeColor}">
                    開盤：<b>${open}</b><br/>
                    最高：<b>${high}</b><br/>
                    最低：<b>${low}</b><br/>
                    收盤：<b>${close}</b>
                </div>
            `;
            }
        },

        // 🛠️ 2. K線圖放大縮小與拖曳功能 (DataZoom)
        dataZoom: [
            {
                type: 'inside',       // 內置型：允許直接在 K 線圖上用「滑鼠滾輪」或「手機雙指」進行放大縮小與左右拖曳
                start: 80,            // 預設載入時，顯示最後面 20% 的資料 (約最近 40 幾天)
                end: 100              // 這樣畫面看起來不會太擠，又可以往前滾動看歷史資料
            },
            {
                type: 'slider',       // 滑桿型：在圖表下方顯示一個實體的滾動縮放條
                show: true,
                start: 80,
                end: 100,
                bottom: '6%',
                borderColor: '#2d3139',
                textStyle: { color: '#8392A5' },
                handleStyle: { color: '#8392A5' } // 縮放手把的顏色
            }
        ],

        xAxis: {
            type: 'category',
            data: dates,
            axisLine: { lineStyle: { color: '#8392A5' } },
            splitLine: { show: false }
        },
        yAxis: {
            scale: true,              // 💡 非常重要：強制 Y 軸不從 0 開始，而是自動對齊股票價格的區間
            axisLine: { lineStyle: { color: '#8392A5' } },
            splitLine: { show: true, lineStyle: { color: '#2d3139' } } // 暗色背景格線
        },
        series: [{
            type: 'candlestick',
            data: kValues,
            itemStyle: {
                color: '#ef5350',     // 漲（紅）
                color0: '#26a69a',    // 跌（綠）
                borderColor: '#ef5350',
                borderColor0: '#26a69a'
            }
        }]
    };
    myChart.setOption(option);
}