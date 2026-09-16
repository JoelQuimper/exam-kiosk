(() => {
    "use strict";

    const protocolVersion = 1;

    function initialize() {
        const startButton = document.getElementById("launcher-start-exam");
        const status = document.getElementById("launcher-status");
        if (!startButton || !status) {
            return;
        }

        const webview = window.chrome?.webview;
        if (!webview) {
            status.textContent = status.dataset.directBrowser;
            return;
        }

        let statusRequestId = crypto.randomUUID();
        let startRequestId = null;

        function post(type, requestId, additionalData = {}) {
            webview.postMessage({
                version: protocolVersion,
                type,
                requestId,
                ...additionalData
            });
        }

        webview.addEventListener("message", event => {
            const message = event.data;
            if (!message || message.version !== protocolVersion) {
                return;
            }

            if (message.type === "agentStatus"
                && message.requestId === statusRequestId) {
                const ready = message.state === "available";
                startButton.disabled = !ready;
                status.textContent = ready
                    ? status.dataset.ready
                    : message.message || status.dataset.unavailable;
                return;
            }

            if (message.type !== "startExamResult"
                || message.requestId !== startRequestId) {
                return;
            }

            if (message.state === "cancelled"
                || message.state === "failed"
                || message.state === "busy") {
                statusRequestId = crypto.randomUUID();
                startRequestId = null;
                status.textContent = message.message || status.dataset.connecting;
                post("clientReady", statusRequestId);
                return;
            }

            status.textContent = message.message || status.dataset.starting;
        });

        startButton.addEventListener("click", () => {
            if (startButton.disabled || startRequestId !== null) {
                return;
            }

            startButton.disabled = true;
            status.textContent = status.dataset.starting;
            startRequestId = crypto.randomUUID();
            post("startExam", startRequestId, {
                exam: {
                    title: startButton.dataset.examTitle
                }
            });
        });

        status.textContent = status.dataset.connecting;
        post("clientReady", statusRequestId);
    }

    if (document.readyState === "loading") {
        document.addEventListener("DOMContentLoaded", initialize, { once: true });
    } else {
        initialize();
    }
})();
