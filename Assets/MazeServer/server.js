const { WebSocketServer } = require('ws');

const PORT = 7777;
const TICK_RATE = 20; // 20 раз в секунду (каждые 50 мс)

const COLS = 12;
const ROWS = 12;
const CELL_SIZE = 4;

// ===== ГЕНЕРАТОР ЛАБИРИНТА =====

function generateArenaMaze(cols = 12, rows = 12, cellSize = 4) {
    const grid = [];
    for (let r = 0; r < rows; r++) {
        grid[r] = [];
        for (let c = 0; c < cols; c++) {
            grid[r][c] = { north: true, south: true, east: true, west: true, visited: false };
        }
    }

    // 1. Построение базовой связной структуры (обход в глубину)
    const stack = [];
    let current = { r: 0, c: 0 };
    grid[0][0].visited = true;

    while (true) {
        const neighbors = [];
        const { r, c } = current;

        if (r > 0 && !grid[r - 1][c].visited) neighbors.push({ r: r - 1, c, dir: 'north', opp: 'south' });
        if (r < rows - 1 && !grid[r + 1][c].visited) neighbors.push({ r: r + 1, c, dir: 'south', opp: 'north' });
        if (c < cols - 1 && !grid[r][c + 1].visited) neighbors.push({ r, c: c + 1, dir: 'east', opp: 'west' });
        if (c > 0 && !grid[r][c - 1].visited) neighbors.push({ r, c: c - 1, dir: 'west', opp: 'east' });

        if (neighbors.length > 0) {
            const next = neighbors[Math.floor(Math.random() * neighbors.length)];
            grid[current.r][current.c][next.dir] = false;
            grid[next.r][next.c][next.opp] = false;
            grid[next.r][next.c].visited = true;
            stack.push(current);
            current = { r: next.r, c: next.c };
        } else if (stack.length > 0) {
            current = stack.pop();
        } else {
            break;
        }
    }

    // 2. Создание открытых комнат (удаление внутренних перегородок)
    // Центральная арена 3x3 ячейки
    carveOpenRoom(grid, Math.floor(rows / 2) - 1, Math.floor(cols / 2) - 1, 3, 3);
    // Дополнительные открытые зоны 2x2 ячейки
    carveOpenRoom(grid, 1, 1, 2, 2);
    carveOpenRoom(grid, rows - 3, cols - 3, 2, 2);

    // 3. Формирование кольцевых проходов (Braid): часть тупиков пробиваем
    for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
            let wallCount = 0;
            if (grid[r][c].north) wallCount++;
            if (grid[r][c].south) wallCount++;
            if (grid[r][c].east) wallCount++;
            if (grid[r][c].west) wallCount++;

            if (wallCount === 3 && Math.random() < 0.4) {
                const candidates = [];
                if (r > 0 && grid[r][c].north) candidates.push({ r1: r, c1: c, dir: 'north', r2: r - 1, c2: c, opp: 'south' });
                if (r < rows - 1 && grid[r][c].south) candidates.push({ r1: r, c1: c, dir: 'south', r2: r + 1, c2: c, opp: 'north' });
                if (c < cols - 1 && grid[r][c].east) candidates.push({ r1: r, c1: c, dir: 'east', r2: r, c2: c + 1, opp: 'west' });
                if (c > 0 && grid[r][c].west) candidates.push({ r1: r, c1: c, dir: 'west', r2: r, c2: c - 1, opp: 'east' });

                if (candidates.length > 0) {
                    const pick = candidates[Math.floor(Math.random() * candidates.length)];
                    grid[pick.r1][pick.c1][pick.dir] = false;
                    grid[pick.r2][pick.c2][pick.opp] = false;
                }
            }
        }
    }

    // 4. Перевод в массив мировых координат
    // Строка r растет вместе с Z, поэтому "north" (r - 1) лежит на стороне меньшего Z.
    const walls = [];
    const offsetX = -(cols * cellSize) / 2;
    const offsetZ = -(rows * cellSize) / 2;
    const half = cellSize / 2;

    for (let r = 0; r < rows; r++) {
        for (let c = 0; c < cols; c++) {
            const centerX = offsetX + c * cellSize + half;
            const centerZ = offsetZ + r * cellSize + half;

            if (grid[r][c].north) {
                walls.push({ x: Number(centerX.toFixed(2)), z: Number((centerZ - half).toFixed(2)), rot: 0 });
            }
            if (grid[r][c].west) {
                walls.push({ x: Number((centerX - half).toFixed(2)), z: Number(centerZ.toFixed(2)), rot: 90 });
            }
            if (r === rows - 1 && grid[r][c].south) {
                walls.push({ x: Number(centerX.toFixed(2)), z: Number((centerZ + half).toFixed(2)), rot: 0 });
            }
            if (c === cols - 1 && grid[r][c].east) {
                walls.push({ x: Number((centerX + half).toFixed(2)), z: Number(centerZ.toFixed(2)), rot: 90 });
            }
        }
    }

    return walls;
}

function carveOpenRoom(grid, startR, startC, h, w) {
    for (let r = startR; r < startR + h; r++) {
        for (let c = startC; c < startC + w; c++) {
            if (r + 1 < startR + h) {
                grid[r][c].south = false;
                grid[r + 1][c].north = false;
            }
            if (c + 1 < startC + w) {
                grid[r][c].east = false;
                grid[r][c + 1].west = false;
            }
        }
    }
}

// ===== СЕТЕВАЯ ЧАСТЬ =====

const wss = new WebSocketServer({ port: PORT });

// Таблица активных сессий: clientId -> { ws, state }
const players = new Map();

// Генерация карты при запуске сервера
const globalWalls = generateArenaMaze(COLS, ROWS, CELL_SIZE);

// Центр центральной комнаты 3x3 (между колоннами, не внутри них)
const SPAWN_CENTER_X = -(COLS * CELL_SIZE) / 2 + (Math.floor(COLS / 2) - 1 + 1.5) * CELL_SIZE;
const SPAWN_CENTER_Z = -(ROWS * CELL_SIZE) / 2 + (Math.floor(ROWS / 2) - 1 + 1.5) * CELL_SIZE;

console.log('==========================================');
console.log(`[СЕРВЕР] Запущен на порту ${PORT}`);
console.log(`[СЕРВЕР] Карта создана. Сегментов стен: ${globalWalls.length}`);
console.log('Ожидание подключений студентов...');
console.log('==========================================');

wss.on('connection', (ws, req) => {
    // 1. Уникальный ID клиента
    const clientId = 'p_' + Math.random().toString(36).substr(2, 8);
    const clientIp = req.socket.remoteAddress;

    // 2. Точка спавна в центре открытой площадки (небольшой разброс)
    const spawnX = Number((SPAWN_CENTER_X + (Math.random() - 0.5) * 1.6).toFixed(2));
    const spawnZ = Number((SPAWN_CENTER_Z + (Math.random() - 0.5) * 1.6).toFixed(2));

    const initialPlayerState = {
        id: clientId,
        name: 'Player',
        x: spawnX,
        y: 0.0,
        z: spawnZ,
        yaw: 0.0,
        pitch: 0.0,
        crouch: false
    };

    players.set(clientId, { ws, state: initialPlayerState });
    console.log(`[ПОДКЛЮЧЕНИЕ] Клиент ${clientId} (IP: ${clientIp}) вошел в сессию`);

    // 3. Отправка клиенту структуры лабиринта и точки спавна
    ws.send(JSON.stringify({
        type: 'INIT_WORLD',
        id: clientId,
        spawnPos: [initialPlayerState.x, initialPlayerState.y, initialPlayerState.z],
        walls: globalWalls
    }));

    // 4. Прием пакетов от Unity-клиента
    ws.on('message', (messageRaw) => {
        try {
            const data = JSON.parse(messageRaw.toString());

            // Регистрация никнейма из LobbyScene
            if (data.type === 'JOIN') {
                const session = players.get(clientId);
                if (session) {
                    session.state.name = data.name || 'Anonymous';
                    console.log(`[РЕГИСТРАЦИЯ] Игрок ${clientId} указал имя: "${session.state.name}"`);
                }
            }

            // Синхронизация движения и взгляда
            if (data.type === 'MOVE') {
                const session = players.get(clientId);
                if (session) {
                    session.state.x = Number(data.x);
                    session.state.y = Number(data.y);
                    session.state.z = Number(data.z);
                    session.state.yaw = Number(data.yaw);
                    session.state.pitch = Number(data.pitch);
                    session.state.crouch = Boolean(data.crouch);
                }
            }
        } catch (err) {
            console.error(`[ОШИБКА JSON] от ${clientId}: `, err.message);
        }
    });

    // 5. Отключение клиента
    ws.on('close', () => {
        const session = players.get(clientId);
        const playerName = session ? session.state.name : clientId;
        players.delete(clientId);
        console.log(`[ОТКЛЮЧЕНИЕ] Игрок "${playerName}" (${clientId}) отключился`);

        // Оповещаем остальных клиентов об удалении аватара
        broadcast({ type: 'PLAYER_LEFT', id: clientId });
    });

    ws.on('error', (err) => {
        console.error(`[ОШИБКА СОКЕТА] ${clientId}: `, err.message);
    });
});

// Игровой цикл: каждые 50 мс рассылаем снапшот мира всем подключенным
setInterval(() => {
    if (players.size === 0) return;

    const snapshot = {
        type: 'SNAPSHOT',
        players: Array.from(players.values()).map(p => p.state)
    };

    broadcast(snapshot);
}, 1000 / TICK_RATE);

function broadcast(packet) {
    const rawData = JSON.stringify(packet);
    for (const { ws } of players.values()) {
        if (ws.readyState === 1) { // WebSocket.OPEN
            ws.send(rawData);
        }
    }
}
