(() => {
  'use strict';
  if (window.reportSceneHost) return;
  const results = new Map();
  let clipboard = '';
  window.reportScene = { ready: false, state: null, tests: null, downloads: [] };
  const complete = (id, value) => results.set(id, value);
  const fail = (id, error) => complete(id, { error: String(error?.message || error) });
  const encode = bytes => {
    const chunks = [];
    for (let offset = 0; offset < bytes.length; offset += 32768)
      chunks.push(String.fromCharCode(...bytes.subarray(offset, offset + 32768)));
    return btoa(chunks.join(''));
  };
  const database = () => new Promise((resolve, reject) => {
    const request = indexedDB.open('ReportScene', 1);
    request.onupgradeneeded = () => request.result.createObjectStore('workspace');
    request.onerror = () => reject(request.error);
    request.onsuccess = () => resolve(request.result);
  });
  window.reportSceneHost = {
    take(id) {
      if (!results.has(id)) return '';
      const value = results.get(id); results.delete(id); return JSON.stringify(value);
    },
    pick(id, accept) {
      const input = document.createElement('input'); input.type = 'file'; input.accept = accept;
      input.style.display = 'none'; input.dataset.reportScene = 'file-picker'; document.body.append(input);
      const dispose = () => input.remove();
      input.addEventListener('cancel', () => { complete(id, { cancelled: true }); dispose(); }, { once: true });
      input.addEventListener('change', async () => {
        try {
          const file = input.files?.[0];
          if (!file) { complete(id, { cancelled: true }); return; }
          if (file.size > 64_000_000) throw new Error('Input exceeds the 64 MB limit.');
          complete(id, { name: file.name, data: encode(new Uint8Array(await file.arrayBuffer())) });
        } catch (error) { fail(id, error); } finally { dispose(); }
      }, { once: true });
      input.click();
    },
    download(name, mediaType, base64) {
      const raw = atob(base64); const bytes = new Uint8Array(raw.length);
      for (let n = 0; n < raw.length; n++) bytes[n] = raw.charCodeAt(n);
      const url = URL.createObjectURL(new Blob([bytes], { type: mediaType }));
      const a = document.createElement('a'); a.href = url; a.download = name; a.style.display = 'none';
      document.body.append(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(url), 60_000);
      window.reportScene.downloads.push({ name, mediaType, size: bytes.length });
      if (window.reportScene.downloads.length > 32) window.reportScene.downloads.shift();
    },
    print(html) {
      const target = window.open('', '_blank');
      if (!target) return 'Printing was blocked by the browser. Allow popups for this site or export HTML and print it.';
      target.opener = null; target.document.open(); target.document.write(html); target.document.close();
      const print = () => { try { target.focus(); target.print(); } catch (error) { console.error(error); } };
      if (target.document.readyState === 'complete') setTimeout(print, 250); else target.addEventListener('load', print, { once: true });
      return 'ok';
    },
    async recover(id) {
      let db;
      try {
        db = await database(); const request = db.transaction('workspace', 'readonly').objectStore('workspace').get('recovery');
        request.onsuccess = () => { complete(id, { text: request.result || null }); db.close(); };
        request.onerror = () => { fail(id, request.error); db.close(); };
      } catch (error) { if (db) db.close(); fail(id, error); }
    },
    async saveRecovery(id, text) {
      let db;
      try {
        db = await database(); const transaction = db.transaction('workspace', 'readwrite'); transaction.objectStore('workspace').put(text, 'recovery');
        transaction.oncomplete = () => { complete(id, {}); db.close(); };
        transaction.onerror = () => { fail(id, transaction.error); db.close(); };
        transaction.onabort = () => { fail(id, transaction.error || 'Recovery transaction aborted'); db.close(); };
      } catch (error) { if (db) db.close(); fail(id, error); }
    },
    async copy(id, text) {
      clipboard = text;
      try { if (navigator.clipboard?.writeText) await navigator.clipboard.writeText(text); }
      catch { /* The internal report-object clipboard remains available without OS clipboard permission. */ }
      complete(id, {});
    },
    async paste(id) {
      try { complete(id, { text: navigator.clipboard?.readText ? await navigator.clipboard.readText() : clipboard }); }
      catch { complete(id, { text: clipboard }); }
    }
  };
})();
