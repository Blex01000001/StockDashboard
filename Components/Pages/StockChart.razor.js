// StockChart.razor.js：技術圖表 = K 線 + 均線 + 附圖 (成交量 / MACD / KD / RSI / 三大法人...) 依序疊在下方
// 附圖定義與組圖程式在 wwwroot/js/candle-chart.js (與型態掃描小卡共用)

// 版本參數沿用本檔網址的 ?v=，讓共用檔也跟著更新 (避免瀏覽器沿用快取中的舊版)
const { UP, DOWN, MA_LINES, SUB_CHARTS, buildStackedOption } = await import('../../js/candle-chart.js' + new URL(import.meta.url).search);

// 版面 (像素)：K 線區高度固定，每多一個附圖，圖表就增高 subLegend + subHeight
const LAYOUT = {
    top: 60,           // 標題與均線圖例
    candleHeight: 340,
    subLegend: 18,
    subHeight: 70,
    bottom: 80,        // 日期標籤 + 縮放滑桿
    left: 60,
    right: 40
};
const DEFAULT_ZOOM = { start: 80, end: 100 };   // 預設顯示最後 20% (約最近 40 幾天)

// candles：PatternCandle 陣列 (camelCase)；visibleMas：預設顯示的均線名稱；subCharts：附圖 (依序)；settings：指標參數
export function renderStockChart(containerId, stockLabel, candles, visibleMas, subCharts, settings) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    const subs = (subCharts || []).filter(s => SUB_CHARTS[s]);
    dom.style.height = (LAYOUT.top + LAYOUT.candleHeight + subs.length * (LAYOUT.subLegend + LAYOUT.subHeight) + LAYOUT.bottom) + 'px';

    // ECharts 由 App.razor 的 <script> 全域載入；重繪時沿用同一個圖表實例，並保留使用者目前的縮放範圍
    let chart = echarts.getInstanceByDom(dom);
    let zoom = DEFAULT_ZOOM;
    if (chart) {
        const current = chart.getOption()?.dataZoom?.[0];
        if (current && chart.__stockLabel === stockLabel) zoom = { start: current.start, end: current.end };
        chart.resize();
    } else {
        chart = echarts.init(dom);
    }
    chart.__stockLabel = stockLabel;   // 換股票時縮放範圍回到預設

    const option = buildStackedOption({
        candles, subs, settings: settings || {}, layout: LAYOUT,
        mainSeries: {
            type: 'candlestick',
            data: candles.map(c => [c.open, c.close, c.low, c.high]),
            itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN }
        },
        tooltipTitle: c => `日期：${c.date}`,
        dateFormatter: d => d,
        maLegend: { top: 30, right: LAYOUT.right }
    });
    option.title = { text: `${stockLabel} 技術分析圖`, textStyle: { color: '#fff' } };
    option.legend[0].selected = Object.fromEntries(MA_LINES.map(m => [m.name, (visibleMas || []).includes(m.name)]));
    // 縮放與拖曳：滑鼠滾輪 / 拖曳 (inside) 與下方滑桿 (slider)，所有附圖一起連動
    option.dataZoom = [
        { type: 'inside', xAxisIndex: 'all', ...zoom },
        {
            type: 'slider', xAxisIndex: 'all', ...zoom, bottom: 12, height: 24,
            borderColor: '#2d3139', textStyle: { color: '#8392A5' }, handleStyle: { color: '#8392A5' }
        }
    ];
    chart.setOption(option, true);
}

export function disposeChart(containerId) {
    const dom = document.getElementById(containerId);
    const chart = dom && echarts.getInstanceByDom(dom);
    if (chart) chart.dispose();
}
