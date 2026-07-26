const databaseName = "diagram-studio";
const documentStoreName = "documents";
const snapshotStoreName = "snapshots";
const libraryStoreName = "library";

export async function listDocuments() {
    const database = await openDatabase();
    return await getAll(database, documentStoreName, value => value.summaryJson);
}

export async function getDocument(documentId) {
    const database = await openDatabase();
    const entry = await runRequest(database, [documentStoreName], "readonly", stores => stores.documents.get(documentId));
    return entry?.documentJson ?? null;
}

export async function saveDocument(documentId, documentJson, summaryJson) {
    const database = await openDatabase();
    await runRequest(database, [documentStoreName], "readwrite", stores =>
        stores.documents.put({
            documentId,
            documentJson,
            summaryJson
        }, documentId));
}

export async function deleteDocument(documentId) {
    const database = await openDatabase();
    // Snapshot cleanup shares the document transaction so a delete cannot leave orphaned snapshots.
    await runRequest(database, [documentStoreName, snapshotStoreName], "readwrite", stores => {
        stores.documents.delete(documentId);
        const snapshotIndex = stores.snapshots.index("byDocumentId");
        const request = snapshotIndex.openCursor(IDBKeyRange.only(documentId));
        request.onsuccess = event => {
            const cursor = event.target.result;
            if (cursor) {
                cursor.delete();
                cursor.continue();
            }
        };

        return stores.documents.get(documentId);
    });
}

export async function listSnapshots(documentId) {
    const database = await openDatabase();
    return await getByIndex(database, snapshotStoreName, "byDocumentId", documentId, value => value.summaryJson);
}

export async function saveSnapshot(documentId, snapshotId, summaryJson, documentJson) {
    const database = await openDatabase();
    await runRequest(database, [snapshotStoreName], "readwrite", stores =>
        stores.snapshots.put({
            snapshotId,
            documentId,
            summaryJson,
            documentJson
        }, snapshotId));
}

export async function getSnapshotDocument(documentId, snapshotId) {
    const database = await openDatabase();
    const entry = await runRequest(database, [snapshotStoreName], "readonly", stores => stores.snapshots.get(snapshotId));
    if (!entry || entry.documentId !== documentId) {
        return null;
    }

    return entry.documentJson ?? null;
}

export async function listLibraryItems() {
    const database = await openDatabase();
    return await getAll(database, libraryStoreName, value => value.itemJson);
}

export async function saveLibraryItem(libraryItemId, itemJson) {
    const database = await openDatabase();
    await runRequest(database, [libraryStoreName], "readwrite", stores =>
        stores.library.put({
            libraryItemId,
            itemJson
        }, libraryItemId));
}

export async function deleteLibraryItem(libraryItemId) {
    const database = await openDatabase();
    await runRequest(database, [libraryStoreName], "readwrite", stores => stores.library.delete(libraryItemId));
}

function openDatabase() {
    return new Promise((resolve, reject) => {
        const request = indexedDB.open(databaseName, 2);
        request.onupgradeneeded = () => {
            const database = request.result;

            if (!database.objectStoreNames.contains(documentStoreName)) {
                database.createObjectStore(documentStoreName);
            }

            if (!database.objectStoreNames.contains(snapshotStoreName)) {
                const snapshots = database.createObjectStore(snapshotStoreName);
                snapshots.createIndex("byDocumentId", "documentId", { unique: false });
            }

            if (!database.objectStoreNames.contains(libraryStoreName)) {
                database.createObjectStore(libraryStoreName);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => reject(request.error);
    });
}

function runRequest(database, storeNames, mode, action) {
    return new Promise((resolve, reject) => {
        const transaction = database.transaction(storeNames, mode);
        const names = Array.isArray(storeNames) ? storeNames : [storeNames];
        const stores = {
            documents: names.includes(documentStoreName) ? transaction.objectStore(documentStoreName) : null,
            snapshots: names.includes(snapshotStoreName) ? transaction.objectStore(snapshotStoreName) : null,
            library: names.includes(libraryStoreName) ? transaction.objectStore(libraryStoreName) : null
        };

        const request = action(stores);
        transaction.oncomplete = () => resolve(request?.result);
        transaction.onerror = () => reject(transaction.error ?? request?.error);
        transaction.onabort = () => reject(transaction.error ?? request?.error);
    });
}

function getAll(database, storeName, selector) {
    return new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, "readonly");
        const store = transaction.objectStore(storeName);
        const request = store.getAll();
        request.onsuccess = () => resolve((request.result ?? []).map(selector));
        request.onerror = () => reject(request.error);
    });
}

function getByIndex(database, storeName, indexName, key, selector) {
    return new Promise((resolve, reject) => {
        const transaction = database.transaction(storeName, "readonly");
        const store = transaction.objectStore(storeName);
        const index = store.index(indexName);
        const request = index.getAll(IDBKeyRange.only(key));
        request.onsuccess = () => resolve((request.result ?? []).map(selector));
        request.onerror = () => reject(request.error);
    });
}
