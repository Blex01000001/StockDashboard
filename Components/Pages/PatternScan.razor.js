// PatternScan.razor.js：繪製型態掃描的小卡與參數旁的型態範例 (K 線 + 均線 + 成交量)

const UP = '#ef5350';     // 漲：紅 (台股配色)
const DOWN = '#26a69a';   // 跌：綠
// 均線：欄位名 (對應 C# PatternCandle 的 camelCase)、圖例名稱、顏色。點圖例可隱藏 / 顯示該條線
// 顏色避開漲紅跌綠，以免和 K 棒混淆
const MA_LINES = [
    { key: 'ma5', name: 'MA5', color: '#f5c242' },
    { key: 'ma10', name: 'MA10', color: '#ab47bc' },
    { key: 'ma20', name: 'MA20', color: '#42a5f5' },
    { key: 'ma60', name: 'MA60', color: '#ff9800' },
    { key: 'ma120', name: 'MA120', color: '#bdbdbd' }
];
const SIGNAL_COLOR = '#ffd54f';   // 型態標示
const HORIZON_COLOR = '#8392A5';  // 觀察日標示

// 附圖的線：成交量均線、MACD 的 DIF / 訊號線
const SUB_LINES = {
    volume: [
        { key: 'volMa5', name: '量MA5', color: '#f5c242' },
        { key: 'volMa10', name: '量MA10', color: '#ab47bc' }
    ],
    macd: [
        { key: 'dif', name: 'DIF', color: '#f5c242' },
        { key: 'macd', name: 'MACD', color: '#42a5f5' }
    ],
    kd: [
        { key: 'k', name: 'K', color: '#f5c242' },
        { key: 'd', name: 'D', color: '#42a5f5' }
    ],
    rsi: [
        { key: 'rsiFast', name: 'RSI6', color: '#f5c242' },
        { key: 'rsiSlow', name: 'RSI12', color: '#42a5f5' }
    ]
};

// 0~100 的擺盪指標：固定 Y 軸範圍，並畫超買 / 超賣參考線
const OSCILLATOR_LEVELS = { kd: [80, 20], rsi: [70, 30] };

let cardCharts = [];
let previewChart = null;

// 離開頁面時釋放所有圖表
export function disposeAll() {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    if (previewChart) { previewChart.dispose(); previewChart = null; }
}

// 換頁或重新掃描時，先釋放上一批小卡圖表，避免記憶體累積
// visibleMas：預設顯示的均線名稱 (例如 ['MA5','MA20'])；其他均線仍在圖例中 (灰色)，點一下可單張顯示
// horizons：觀察天數 (例如 [5, 10, 20, 60])，在型態成立日之後第 N 根 K 棒畫淡色直線
// subChart：附圖內容，'volume' (成交量 + 量均線)、'macd' (DIF、MACD 訊號線、OSC 柱狀)、'kd' (K、D)、'rsi' (RSI6、RSI12)
export function renderPatternCards(cards, visibleMas, horizons, subChart) {
    cardCharts.forEach(c => c.dispose());
    cardCharts = [];
    const selected = Object.fromEntries(MA_LINES.map(m => [m.name, (visibleMas || []).includes(m.name)]));
    for (const card of cards) {
        const dom = document.getElementById(card.containerId);
        if (!dom) continue;
        const chart = echarts.init(dom);
        const option = buildOption(card.candles, card.signalIndex, card.barCount,
            { showMa: true, showAxisLabels: true, horizons: horizons || [], subChart: subChart || 'volume' });
        option.legend[0].selected = selected;
        chart.setOption(option);
        cardCharts.push(chart);
    }
}

// 型態範例：參數改變時重畫 (沿用同一個圖表實例)
export function renderPreview(containerId, candles, signalIndex, barCount) {
    const dom = document.getElementById(containerId);
    if (!dom) return;
    if (!previewChart || previewChart.getDom() !== dom) {
        if (previewChart) previewChart.dispose();
        previewChart = echarts.init(dom);
    }
    previewChart.setOption(buildOption(candles, signalIndex, barCount, { showMa: false, showAxisLabels: false }), true);
}

function buildOption(candles, signalIndex, barCount, { showMa, showAxisLabels, horizons = [], subChart = 'volume' }) {
    const dates = candles.map(c => c.date);
    const signalDate = dates[signalIndex];
    const axisLine = { lineStyle: { color: '#8392A5' } };
    const firstPatternBar = Math.max(0, signalIndex - barCount + 1);

    // 型態的每一根 K 棒下方放黃色三角形
    const patternMarks = [];
    for (let i = firstPatternBar; i <= signalIndex; i++) {
        patternMarks.push({ coord: [dates[i], candles[i].low] });
    }

    const series = [
        {
            type: 'candlestick',
            data: candles.map(c => [c.open, c.close, c.low, c.high]),
            itemStyle: { color: UP, color0: DOWN, borderColor: UP, borderColor0: DOWN },
            // 型態成立日：黃色虛線；型態每根 K 棒：下方三角形
            // (markArea 起訖為同一類別時寬度為 0 不會顯示，所以不用它)
            markLine: {
                silent: true, symbol: 'none', label: { show: false },
                lineStyle: { color: SIGNAL_COLOR, type: 'dashed', width: 1 },
                data: [
                    { xAxis: signalDate },
                    // 觀察日：型態成立後第 N 個交易日 (之後報酬就是算到這一天的收盤)；超出資料範圍的不畫
                    ...horizons
                        .filter(h => signalIndex + h < dates.length)
                        .map(h => ({
                            xAxis: dates[signalIndex + h],
                            lineStyle: { color: HORIZON_COLOR, type: 'dotted', width: 1, opacity: 0.55 },
                            label: { show: true, formatter: `${h}日`, position: 'end', color: HORIZON_COLOR, fontSize: 9, distance: 2 }
                        }))
                ]
            },
            markPoint: {
                silent: true, symbol: 'triangle', symbolSize: 8, symbolOffset: [0, 11],
                label: { show: false }, itemStyle: { color: SIGNAL_COLOR },
                data: patternMarks
            }
        },
    ];
    // 附圖柱狀：成交量 (紅漲綠跌)、MACD 的 OSC (正紅負綠)；KD、RSI 只有線
    // 預覽圖 (showAxisLabels = false) 一律畫單純的成交量
    const sub = showAxisLabels ? subChart : 'volume';
    if (sub === 'macd') {
        series.push({ name: 'OSC', type: 'bar', xAxisIndex: 1, yAxisIndex: 1,
                      data: candles.map(c => ({ value: c.osc, itemStyle: { color: (c.osc ?? 0) >= 0 ? UP : DOWN } })) });
    } else if (sub === 'volume') {
        series.push({ type: 'bar', xAxisIndex: 1, yAxisIndex: 1,
                      data: candles.map(c => ({ value: c.volume, itemStyle: { color: c.close >= c.open ? UP : DOWN } })) });
    }
    // 附圖的線 (只在小卡上畫)
    const subLines = showAxisLabels ? SUB_LINES[sub] || [] : [];
    const levels = OSCILLATOR_LEVELS[sub];
    subLines.forEach((l, i) => {
        series.push({
            name: l.name, type: 'line', xAxisIndex: 1, yAxisIndex: 1, data: candles.map(c => c[l.key]),
            showSymbol: false, lineStyle: { width: 1, color: l.color }, itemStyle: { color: l.color },
            // 超買 / 超賣參考線 (掛在第一條線上即可)
            markLine: levels && i === 0 ? {
                silent: true, symbol: 'none', label: { show: true, position: 'start', color: '#5d6270', fontSize: 9, distance: 2 },
                lineStyle: { color: '#5d6270', type: 'dashed', width: 1 },
                data: levels.map(v => ({ yAxis: v }))
            } : undefined
        });
    });
    if (showMa) {
        for (const m of MA_LINES) {
            series.push({ name: m.name, type: 'line', data: candles.map(c => c[m.key]), showSymbol: false,
                          lineStyle: { width: 1, color: m.color }, itemStyle: { color: m.color } });
        }
    }

    return {
        animation: false,
        legend: showMa
            ? [
                { data: MA_LINES.map(m => m.name), top: 0, right: 8, itemWidth: 12, itemHeight: 7, itemGap: 8,
                  textStyle: { color: '#8392A5', fontSize: 10 },
                  // 隱藏中的均線在圖例上調暗 (預設淺灰色會和 MA120 的灰色混淆)
                  inactiveColor: '#3a3f4b', inactiveBorderColor: '#3a3f4b' },
                // 附圖圖例：放在附圖左上方
                { data: subLines.map(l => l.name), top: '68%', left: 48, itemWidth: 12, itemHeight: 7, itemGap: 8,
                  textStyle: { color: '#8392A5', fontSize: 10 }, inactiveColor: '#3a3f4b', inactiveBorderColor: '#3a3f4b' }
              ]
            : undefined,
        tooltip: showAxisLabels ? {
            trigger: 'axis',
            axisPointer: { type: 'cross' },
            backgroundColor: 'rgba(30, 34, 45, 0.9)',
            borderColor: '#434651',
            textStyle: { color: '#d1d4dc', fontSize: 12 },
            formatter: params => {
                const c = candles[params[0].dataIndex];
                const color = c.close >= c.open ? UP : DOWN;
                return `<div style="font-weight:bold;color:#fff;">${c.date}${c.date === signalDate ? '（型態成立）' : ''}</div>
                    <div style="color:${color}">開 ${c.open}　高 ${c.high}<br/>低 ${c.low}　收 ${c.close}</div>
                    <div>量 ${c.volume.toLocaleString()}</div>
                    <div>${({
                        macd: `DIF ${c.dif ?? '-'}　MACD ${c.macd ?? '-'}　OSC ${c.osc ?? '-'}`,
                        kd: `K ${c.k ?? '-'}　D ${c.d ?? '-'}`,
                        rsi: `RSI6 ${c.rsiFast ?? '-'}　RSI12 ${c.rsiSlow ?? '-'}`
                    })[subChart] ?? `量MA5 ${c.volMa5?.toLocaleString() ?? '-'}　量MA10 ${c.volMa10?.toLocaleString() ?? '-'}`}</div>
                    ${MA_LINES.map(m => `<div style="color:${m.color}">${m.name} ${c[m.key] ?? '-'}</div>`).join('')}`;
            }
        } : undefined,
        axisPointer: { link: [{ xAxisIndex: 'all' }] },
        grid: showAxisLabels
            // K 線區上方留一列給觀察日標籤 (5日、10日…)，避免和右上角的均線圖例重疊
            ? [{ left: 48, right: 12, top: 36, height: '54%' }, { left: 48, right: 12, top: '76%', height: '14%' }]
            : [{ left: 6, right: 6, top: 6, height: '68%' }, { left: 6, right: 6, top: '80%', height: '16%' }],
        xAxis: [
            { type: 'category', data: dates, axisLine, axisLabel: { show: false }, axisTick: { show: false } },
            { type: 'category', gridIndex: 1, data: dates, axisLine, axisTick: { show: false },
              axisLabel: { show: showAxisLabels, color: '#8392A5', fontSize: 10, formatter: d => d.slice(5) } }
        ],
        yAxis: [
            { scale: true, axisLine, axisLabel: { show: showAxisLabels, color: '#8392A5', fontSize: 10 },
              splitLine: { show: showAxisLabels, lineStyle: { color: '#2d3139' } } },
            { gridIndex: 1, axisLabel: { show: false }, axisLine: { show: false }, splitLine: { show: false },
              // KD、RSI 固定 0~100，參考線位置才有意義
              ...(levels ? { min: 0, max: 100 } : {}) }
        ],
        series
    };
}
