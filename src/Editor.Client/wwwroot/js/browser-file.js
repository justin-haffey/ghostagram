export function downloadText(fileName, text, contentType = "text/plain;charset=utf-8") {
    const blob = new Blob([text], { type: contentType });
    const url = URL.createObjectURL(blob);
    const anchor = document.createElement("a");
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    setTimeout(() => URL.revokeObjectURL(url), 0);
}

