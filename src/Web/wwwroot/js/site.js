(function () {
    'use strict';

    const chatMessages = document.getElementById('chat-messages');
    const chatForm = document.getElementById('chat-form');
    const questionInput = document.getElementById('question-input');
    const btnSend = document.getElementById('btn-send');
    const btnReindex = document.getElementById('btn-reindex');
    const debugToggle = document.getElementById('debug-toggle');
    const debugPanel = document.getElementById('debug-panel');
    const debugContent = document.getElementById('debug-content');
    const documentList = document.getElementById('document-list');

    let isProcessing = false;

    // --- Init ---
    loadDocuments();

    debugToggle.addEventListener('change', () => {
        debugPanel.classList.toggle('hidden', !debugToggle.checked);
    });

    chatForm.addEventListener('submit', (e) => {
        e.preventDefault();
        const question = questionInput.value.trim();
        if (question && !isProcessing) {
            sendQuestion(question);
        }
    });

    btnReindex.addEventListener('click', () => {
        if (!isProcessing) reindex();
    });

    // --- Chat ---
    async function sendQuestion(question) {
        isProcessing = true;
        btnSend.disabled = true;
        questionInput.disabled = true;

        // Remove welcome message
        const welcome = chatMessages.querySelector('.welcome-message');
        if (welcome) welcome.remove();

        // Add user message
        appendUserMessage(question);
        questionInput.value = '';

        // Show loading
        const loadingEl = appendLoading();
        scrollToBottom();

        try {
            const response = await fetch('/api/chat', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    question: question,
                    debugMode: debugToggle.checked
                })
            });

            if (!response.ok) {
                const err = await response.json().catch(() => ({ error: 'Request failed' }));
                throw new Error(err.error || `HTTP ${response.status}`);
            }

            const data = await response.json();
            loadingEl.remove();
            appendAssistantMessage(data);

            if (data.debug && debugToggle.checked) {
                renderDebugInfo(data.debug);
            }

        } catch (err) {
            loadingEl.remove();
            appendError(err.message);
        } finally {
            isProcessing = false;
            btnSend.disabled = false;
            questionInput.disabled = false;
            questionInput.focus();
            scrollToBottom();
        }
    }

    function appendUserMessage(question) {
        const div = document.createElement('div');
        div.className = 'chat-entry';
        div.innerHTML = `<div class="user-question">${escapeHtml(question)}</div>`;
        chatMessages.appendChild(div);
    }

    function appendAssistantMessage(data) {
        const div = document.createElement('div');
        div.className = 'chat-entry';

        let html = `<div class="assistant-answer">${renderMarkdown(data.answer)}`;

        if (data.citations && data.citations.length > 0) {
            html += `<div class="citations">
                <div class="citations-header">Sources</div>`;
            data.citations.forEach((c, i) => {
                html += `<div class="citation-item">
                    <span class="citation-badge">${escapeHtml(c.sourceId || String(i + 1))}</span>
                    <div class="citation-text">
                        <span class="citation-filename">${escapeHtml(c.fileName)}</span>
                        <span class="citation-section"> &mdash; ${escapeHtml(c.section)}</span>
                        <div class="citation-snippet">${escapeHtml(c.snippet)}</div>
                    </div>
                </div>`;
            });
            html += `</div>`;
        }

        html += `</div>`;
        div.innerHTML = html;
        chatMessages.appendChild(div);
    }

    function appendLoading() {
        const div = document.createElement('div');
        div.className = 'chat-entry';
        div.innerHTML = `<div class="loading-indicator">
            <div class="loading-dots">
                <span></span><span></span><span></span>
            </div>
            <span class="loading-text">Searching documents and generating answer...</span>
        </div>`;
        chatMessages.appendChild(div);
        return div;
    }

    function appendError(message) {
        const div = document.createElement('div');
        div.className = 'chat-entry';
        div.innerHTML = `<div class="error-message">Error: ${escapeHtml(message)}</div>`;
        chatMessages.appendChild(div);
    }

    // --- Debug Panel ---
    function renderDebugInfo(debug) {
        let html = '<h4 style="margin-bottom:12px;">Retrieved Chunks</h4>';

        if (debug.retrievedChunks && debug.retrievedChunks.length > 0) {
            debug.retrievedChunks.forEach((chunk, i) => {
                html += `<div class="debug-chunk">
                    <div class="debug-chunk-header">
                        <span class="debug-chunk-id">#${i + 1} ${escapeHtml(chunk.chunkId)}</span>
                        <span class="debug-score">${chunk.score.toFixed(4)}</span>
                    </div>
                    <div class="debug-chunk-meta">${escapeHtml(chunk.fileName)} &mdash; ${escapeHtml(chunk.section)}</div>
                    <div class="debug-chunk-content">${escapeHtml(chunk.contentPreview)}</div>
                </div>`;
            });
        } else {
            html += '<p class="muted">No chunks retrieved.</p>';
        }

        if (debug.fullPrompt) {
            html += `<div class="debug-prompt-section">
                <h4>Full Prompt Sent to LLM</h4>
                <div class="debug-prompt-text">${escapeHtml(debug.fullPrompt)}</div>
            </div>`;
        }

        debugContent.innerHTML = html;
    }

    // --- Documents ---
    async function loadDocuments() {
        try {
            const response = await fetch('/api/index/documents');
            if (!response.ok) throw new Error('Failed to load documents');

            const documents = await response.json();

            if (documents.length === 0) {
                documentList.innerHTML = '<p class="muted">No documents indexed yet. Click Reindex to get started.</p>';
                return;
            }

            let html = '';
            documents.forEach(doc => {
                html += `<div class="doc-item">
                    <div class="doc-item-title">${escapeHtml(doc.title)}</div>
                    <div class="doc-item-meta">${escapeHtml(doc.fileName)} &bull; ${doc.chunkCount} chunks</div>
                </div>`;
            });
            documentList.innerHTML = html;

        } catch {
            documentList.innerHTML = '<p class="muted">Could not load document list.</p>';
        }
    }

    // --- Reindex ---
    async function reindex() {
        isProcessing = true;
        btnReindex.disabled = true;
        btnReindex.innerHTML = '<span class="btn-icon">&#x21bb;</span> Indexing...';
        showToast('Indexing sample data...', 'info');

        try {
            const response = await fetch('/api/index/reindex', { method: 'POST' });
            if (!response.ok) {
                const err = await response.json().catch(() => ({ error: 'Indexing failed' }));
                throw new Error(err.error || `HTTP ${response.status}`);
            }

            const data = await response.json();
            showToast(`${data.message} (${data.chunksIndexed} chunks)`, 'success');
            await loadDocuments();

        } catch (err) {
            showToast(`Indexing failed: ${err.message}`, 'error');
        } finally {
            isProcessing = false;
            btnReindex.disabled = false;
            btnReindex.innerHTML = '<span class="btn-icon">&#x21bb;</span> Reindex';
        }
    }

    // --- Toast ---
    function showToast(message, type) {
        const toast = document.getElementById('toast');
        toast.textContent = message;
        toast.className = `toast ${type}`;
        setTimeout(() => toast.classList.add('hidden'), 4000);
    }

    // --- Utilities ---
    function escapeHtml(str) {
        const div = document.createElement('div');
        div.textContent = str;
        return div.innerHTML;
    }

    function renderMarkdown(text) {
        // Lightweight markdown rendering for common patterns
        let html = escapeHtml(text);

        // Code blocks
        html = html.replace(/```(\w*)\n([\s\S]*?)```/g, '<pre><code>$2</code></pre>');
        // Inline code
        html = html.replace(/`([^`]+)`/g, '<code>$1</code>');
        // Bold
        html = html.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
        // Italic
        html = html.replace(/\*([^*]+)\*/g, '<em>$1</em>');
        // Unordered lists
        html = html.replace(/^- (.+)$/gm, '<li>$1</li>');
        html = html.replace(/(<li>.*<\/li>\n?)+/g, '<ul>$&</ul>');
        // Line breaks to paragraphs
        html = html.replace(/\n\n/g, '</p><p>');
        html = '<p>' + html + '</p>';
        // Clean up empty paragraphs
        html = html.replace(/<p>\s*<\/p>/g, '');

        return html;
    }

    function scrollToBottom() {
        chatMessages.scrollTop = chatMessages.scrollHeight;
    }

    // Global function for example question buttons
    window.askExample = function (btn) {
        questionInput.value = btn.textContent;
        chatForm.dispatchEvent(new Event('submit'));
    };

    window.closeDebugPanel = function () {
        debugToggle.checked = false;
        debugPanel.classList.add('hidden');
    };
})();
