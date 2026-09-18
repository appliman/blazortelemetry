const instances = new Map();
let loader;

function ensureEcharts() {
    if (window.echarts) return Promise.resolve();
    if (loader) return loader;
    loader = new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = '/_content/BlazorTelemetry/vendor/echarts.min.js';
        script.onload = resolve;
        script.onerror = reject;
        document.head.appendChild(script);
    });
    return loader;
}

export async function render(elementId, labels, values, secondaryValues, seriesLabel, secondarySeriesLabel, color, secondaryColor, unit, chartType, showValues) {
    await ensureEcharts();
    const element = document.getElementById(elementId);
    if (!element) return;
    let chart = instances.get(elementId);
    if (!chart) {
        chart = window.echarts.init(element, null, { renderer: 'canvas' });
        instances.set(elementId, chart);
        new ResizeObserver(() => chart.resize()).observe(element);
    }
    const hasSecondarySeries = secondaryValues.length > 0;
    const isBarChart = chartType === 'bar';
    const series = [{
        name: seriesLabel,
        type: isBarChart ? 'bar' : 'line',
        data: values,
        smooth: isBarChart ? false : 0.28,
        showSymbol: false,
        lineStyle: isBarChart ? undefined : { color, width: 2 },
        areaStyle: isBarChart ? undefined : { color: `${color}20` },
        itemStyle: isBarChart ? { color, borderRadius: [4, 4, 0, 0] } : undefined,
        barMaxWidth: isBarChart ? 42 : undefined,
        label: {
            show: isBarChart && showValues,
            position: 'top',
            color: '#dfe7ec',
            fontSize: 11,
            fontWeight: 600,
            formatter: value => Number(value.value).toLocaleString('en-US')
        }
    }];
    if (hasSecondarySeries) {
        series.push({ name: secondarySeriesLabel, type: 'line', data: secondaryValues, smooth: 0.28, showSymbol: false, lineStyle: { color: secondaryColor, width: 2 } });
    }
    chart.setOption({
        animationDuration: 450,
        animationEasing: 'cubicOut',
        grid: { left: 16, right: 18, top: hasSecondarySeries ? 42 : isBarChart && showValues ? 34 : 18, bottom: 28, containLabel: true },
        legend: { show: hasSecondarySeries, top: 5, right: 18, textStyle: { color: '#aab5bf' }, itemWidth: 18, itemHeight: 3 },
        tooltip: { trigger: 'axis', backgroundColor: '#151b22', borderColor: '#39434f', textStyle: { color: '#eef4f8' }, valueFormatter: value => `${value}${unit ? ` ${unit}` : ''}` },
        xAxis: { type: 'category', data: labels, boundaryGap: isBarChart, axisLine: { lineStyle: { color: '#39434f' } }, axisLabel: { color: '#aab5bf', hideOverlap: true, fontWeight: isBarChart ? 600 : 400 }, axisTick: { show: false } },
        yAxis: { type: 'value', minInterval: isBarChart ? 1 : 0, splitLine: { lineStyle: { color: '#242c35' } }, axisLabel: { color: '#8f9daa' } },
        series
    }, true);
}

export function dispose(elementId) {
    const chart = instances.get(elementId);
    if (chart) chart.dispose();
    instances.delete(elementId);
}

export function focusById(elementId) {
    document.getElementById(elementId)?.focus({ preventScroll: true });
}
