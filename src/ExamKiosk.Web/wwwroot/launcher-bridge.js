(() => {
    "use strict";

    const protocolVersion = 2;

    function initialize() {
        const startButtons = Array.from(
            document.querySelectorAll(".launcher-start-exam"));
        const status = document.getElementById("launcher-status");
        if (startButtons.length === 0 || !status) {
            return;
        }

        const webview = window.chrome?.webview;
        if (!webview) {
            status.textContent = status.dataset.directBrowser;
            return;
        }

        let statusRequestId = crypto.randomUUID();
        let startRequestId = null;

        function setStartButtonsDisabled(disabled) {
            startButtons.forEach(button => {
                button.disabled = disabled;
            });
        }

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
                setStartButtonsDisabled(!ready);
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

        startButtons.forEach(startButton => {
            startButton.addEventListener("click", async () => {
                if (startButton.disabled || startRequestId !== null) {
                    return;
                }

                setStartButtonsDisabled(true);
                status.textContent = status.dataset.starting;
                startRequestId = crypto.randomUUID();
                try {
                    const assignmentId = startButton.dataset.assignmentId;
                    const response = await fetch(
                        `/api/v1/exam-assignments/${encodeURIComponent(assignmentId)}/profile`,
                        {
                            credentials: "same-origin",
                            headers: { "Accept": "application/json" }
                        });
                    if (!response.ok) {
                        throw new Error(`Profile request failed with status ${response.status}.`);
                    }

                    const profile = await response.json();
                    post("startExam", startRequestId, {
                        exam: {
                            title: startButton.dataset.examTitle,
                            profile
                        }
                    });
                } catch (error) {
                    console.error("Unable to load the effective exam profile.", error);
                    statusRequestId = crypto.randomUUID();
                    startRequestId = null;
                    status.textContent = status.dataset.unavailable;
                    post("clientReady", statusRequestId);
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
