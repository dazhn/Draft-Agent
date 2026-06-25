/*
 * Minimal Animate span test:
 * one document, one layer, one editable subtitle, 100 frames.
 */
fl.createDocument();

var dom = fl.getDocumentDOM();
if (!dom) {
    alert("无法创建 Animate 文档。");
} else {
    dom.width = 1920;
    dom.height = 1080;
    dom.frameRate = 30;

    var timeline = dom.getTimeline();
    timeline.currentLayer = 0;
    timeline.layers[0].name = "01 文本";
    timeline.currentFrame = 0;

    var bounds = {
        left: Math.round(dom.width * 0.08),
        top: Math.round(dom.height * 0.78),
        right: Math.round(dom.width * 0.92),
        bottom: Math.round(dom.height * 0.92)
    };
    var text = "单条字幕持续 100 帧测试";

    timeline.currentFrame = 0;
    timeline.convertToBlankKeyframes(0, 1);
    timeline.insertFrames(99, false, 0);
    timeline.currentFrame = 0;

    dom.addNewText(bounds, text);
    var frame = timeline.layers[0].frames[0];
    var textElement = frame.elements[frame.elements.length - 1];
    textElement.textType = "static";
    textElement.orientation = "horizontal";
    textElement.useDeviceFonts = true;
    textElement.setTextString(text);
    textElement.setTextAttr("face", "Microsoft YaHei", 0, text.length);
    textElement.setTextAttr("size", 48, 0, text.length);
    textElement.setTextAttr("alignment", "center", 0, text.length);
    textElement.setTextAttr("fillColor", "#0066CC", 0, text.length);

    timeline.currentFrame = 0;

    fl.trace("Draft Agent single subtitle span check: expected=100, animateDuration=" + frame.duration + ", frameCount=" + timeline.frameCount);
}
