-- Seed idempotente para testar o Modo Campeonato em desenvolvimento.
-- Execute somente em um banco local/descartável já migrado.
-- Não cria tokens de push, sessões, notificações ou credenciais externas.

BEGIN;

INSERT INTO "Users"
    ("Id", "UserName", "FirstName", "LastName", "Email", "Password", "Role", "Status", "CreateDate")
VALUES
    ('c2000000-0000-4000-8000-000000000001', 'luis_teste',     'Luis',      'Teste',     'luis.teste@bratnavafc.test',     'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000002', 'ana_teste',      'Ana',       'Costa',     'ana.teste@bratnavafc.test',      'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000003', 'bruno_teste',    'Bruno',     'Alves',     'bruno.teste@bratnavafc.test',    'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000004', 'caio_teste',     'Caio',      'Lima',      'caio.teste@bratnavafc.test',     'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000005', 'daniel_teste',   'Daniel',    'Rocha',     'daniel.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000006', 'eduardo_teste',  'Eduardo',   'Melo',      'eduardo.teste@bratnavafc.test',  'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000007', 'felipe_teste',   'Felipe',    'Dias',      'felipe.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000008', 'gabriel_teste',  'Gabriel',   'Nunes',     'gabriel.teste@bratnavafc.test',  'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000009', 'heitor_teste',   'Heitor',    'Pires',     'heitor.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000010', 'igor_teste',     'Igor',      'Ramos',     'igor.teste@bratnavafc.test',     'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000011', 'joao_teste',     'Joao',      'Freitas',   'joao.teste@bratnavafc.test',     'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000012', 'kaue_teste',     'Kaue',      'Barros',    'kaue.teste@bratnavafc.test',     'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000013', 'leandro_teste',  'Leandro',   'Moura',     'leandro.teste@bratnavafc.test',  'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000014', 'marcos_teste',   'Marcos',    'Silva',     'marcos.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000015', 'natan_teste',    'Natan',     'Souza',     'natan.teste@bratnavafc.test',    'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000016', 'otavio_teste',   'Otavio',    'Gomes',     'otavio.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000017', 'paulo_teste',    'Paulo',     'Castro',    'paulo.teste@bratnavafc.test',    'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000018', 'rafael_teste',   'Rafael',    'Araujo',    'rafael.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000019', 'samuel_teste',   'Samuel',    'Teixeira',  'samuel.teste@bratnavafc.test',   'SECURED_BY_FIREBASE', 1, 1, NOW()),
    ('c2000000-0000-4000-8000-000000000020', 'tiago_teste',    'Tiago',     'Vieira',    'tiago.teste@bratnavafc.test',    'SECURED_BY_FIREBASE', 1, 1, NOW())
ON CONFLICT ("Id") DO UPDATE SET
    "UserName" = EXCLUDED."UserName",
    "FirstName" = EXCLUDED."FirstName",
    "LastName" = EXCLUDED."LastName",
    "Email" = EXCLUDED."Email",
    "Status" = EXCLUDED."Status";

INSERT INTO "Groups"
    ("Id", "Name", "Status", "CreateDate", "CreatedByUserId")
VALUES
    ('c1000000-0000-4000-8000-000000000001', 'BratnavaFC Teste - Campeonato', 1, NOW(), 'c2000000-0000-4000-8000-000000000001')
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Status" = EXCLUDED."Status",
    "CreatedByUserId" = EXCLUDED."CreatedByUserId";

INSERT INTO "GroupAdmins" ("UserId", "GroupId") VALUES
    ('c2000000-0000-4000-8000-000000000001', 'c1000000-0000-4000-8000-000000000001'),
    ('c2000000-0000-4000-8000-000000000002', 'c1000000-0000-4000-8000-000000000001')
ON CONFLICT DO NOTHING;

INSERT INTO "GroupFinanceiros" ("UserId", "GroupId") VALUES
    ('c2000000-0000-4000-8000-000000000002', 'c1000000-0000-4000-8000-000000000001')
ON CONFLICT DO NOTHING;

INSERT INTO "GroupSettings"
    ("Id", "GroupId", "MinPlayers", "MaxPlayers", "DefaultPlaceName", "DefaultDayOfWeek",
     "DefaultKickoffTime", "CreateDate", "AssistIcon", "GoalIcon", "GoalkeeperIcon", "MvpIcon",
     "OwnGoalIcon", "PlayerIcon", "MonthlyFee", "PaymentMode", "MvpTieMaxPlayers", "MvpTieRule",
     "ShowPlayerStats", "Rank1Icon", "Rank2Icon", "Rank3Icon", "GoalkeeperMonthlyFee",
     "ShowStatsClassificationTab", "ShowStatsGeneralTab", "ShowStatsPerMatchTab")
VALUES
    ('c1000000-0000-4000-8000-000000000002', 'c1000000-0000-4000-8000-000000000001',
     6, 24, 'Arena Bratnava Teste', 0, INTERVAL '18 hours', NOW(),
     '🎯', '⚽', '🧤', '🏅', '🥴', '👤', 50.00, 0, 3, 1, TRUE,
     '🥇', '🥈', '🥉', 25.00, TRUE, TRUE, TRUE)
ON CONFLICT ("Id") DO UPDATE SET
    "MinPlayers" = EXCLUDED."MinPlayers",
    "MaxPlayers" = EXCLUDED."MaxPlayers",
    "DefaultPlaceName" = EXCLUDED."DefaultPlaceName",
    "AssistIcon" = EXCLUDED."AssistIcon",
    "GoalIcon" = EXCLUDED."GoalIcon",
    "GoalkeeperIcon" = EXCLUDED."GoalkeeperIcon",
    "MvpIcon" = EXCLUDED."MvpIcon",
    "OwnGoalIcon" = EXCLUDED."OwnGoalIcon",
    "PlayerIcon" = EXCLUDED."PlayerIcon",
    "ShowPlayerStats" = EXCLUDED."ShowPlayerStats";

INSERT INTO "Players"
    ("Id", "Name", "UserId", "GroupId", "SkillPoints", "Status", "IsGoalkeeper", "IsGuest",
     "CreateDate", "JoinedAt", "AttackRating", "DefenseRating", "OverallRating")
VALUES
    ('c3000000-0000-4000-8000-000000000001', 'Luis',     'c2000000-0000-4000-8000-000000000001', 'c1000000-0000-4000-8000-000000000001', 8.8, 1, FALSE, FALSE, NOW(), NOW(), 9, 8, 9),
    ('c3000000-0000-4000-8000-000000000002', 'Ana',      'c2000000-0000-4000-8000-000000000002', 'c1000000-0000-4000-8000-000000000001', 8.4, 1, FALSE, FALSE, NOW(), NOW(), 8, 8, 9),
    ('c3000000-0000-4000-8000-000000000003', 'Bruno',    'c2000000-0000-4000-8000-000000000003', 'c1000000-0000-4000-8000-000000000001', 8.1, 1, TRUE,  FALSE, NOW(), NOW(), 5, 9, 8),
    ('c3000000-0000-4000-8000-000000000004', 'Caio',     'c2000000-0000-4000-8000-000000000004', 'c1000000-0000-4000-8000-000000000001', 7.9, 1, FALSE, FALSE, NOW(), NOW(), 8, 7, 8),
    ('c3000000-0000-4000-8000-000000000005', 'Daniel',   'c2000000-0000-4000-8000-000000000005', 'c1000000-0000-4000-8000-000000000001', 7.7, 1, FALSE, FALSE, NOW(), NOW(), 7, 8, 8),
    ('c3000000-0000-4000-8000-000000000006', 'Eduardo',  'c2000000-0000-4000-8000-000000000006', 'c1000000-0000-4000-8000-000000000001', 7.5, 1, FALSE, FALSE, NOW(), NOW(), 8, 6, 7),
    ('c3000000-0000-4000-8000-000000000007', 'Felipe',   'c2000000-0000-4000-8000-000000000007', 'c1000000-0000-4000-8000-000000000001', 7.4, 1, TRUE,  FALSE, NOW(), NOW(), 4, 9, 8),
    ('c3000000-0000-4000-8000-000000000008', 'Gabriel',  'c2000000-0000-4000-8000-000000000008', 'c1000000-0000-4000-8000-000000000001', 7.2, 1, FALSE, FALSE, NOW(), NOW(), 7, 7, 7),
    ('c3000000-0000-4000-8000-000000000009', 'Heitor',   'c2000000-0000-4000-8000-000000000009', 'c1000000-0000-4000-8000-000000000001', 7.0, 1, FALSE, FALSE, NOW(), NOW(), 8, 5, 7),
    ('c3000000-0000-4000-8000-000000000010', 'Igor',     'c2000000-0000-4000-8000-000000000010', 'c1000000-0000-4000-8000-000000000001', 6.9, 1, FALSE, FALSE, NOW(), NOW(), 6, 8, 7),
    ('c3000000-0000-4000-8000-000000000011', 'Joao',     'c2000000-0000-4000-8000-000000000011', 'c1000000-0000-4000-8000-000000000001', 6.7, 1, TRUE,  FALSE, NOW(), NOW(), 4, 8, 7),
    ('c3000000-0000-4000-8000-000000000012', 'Kaue',     'c2000000-0000-4000-8000-000000000012', 'c1000000-0000-4000-8000-000000000001', 6.6, 1, FALSE, FALSE, NOW(), NOW(), 7, 6, 7),
    ('c3000000-0000-4000-8000-000000000013', 'Leandro',  'c2000000-0000-4000-8000-000000000013', 'c1000000-0000-4000-8000-000000000001', 6.4, 1, FALSE, FALSE, NOW(), NOW(), 6, 7, 6),
    ('c3000000-0000-4000-8000-000000000014', 'Marcos',   'c2000000-0000-4000-8000-000000000014', 'c1000000-0000-4000-8000-000000000001', 6.2, 1, FALSE, FALSE, NOW(), NOW(), 7, 5, 6),
    ('c3000000-0000-4000-8000-000000000015', 'Natan',    'c2000000-0000-4000-8000-000000000015', 'c1000000-0000-4000-8000-000000000001', 6.0, 1, TRUE,  FALSE, NOW(), NOW(), 3, 8, 7),
    ('c3000000-0000-4000-8000-000000000016', 'Otavio',   'c2000000-0000-4000-8000-000000000016', 'c1000000-0000-4000-8000-000000000001', 5.9, 1, FALSE, FALSE, NOW(), NOW(), 6, 6, 6),
    ('c3000000-0000-4000-8000-000000000017', 'Paulo',    'c2000000-0000-4000-8000-000000000017', 'c1000000-0000-4000-8000-000000000001', 5.7, 1, FALSE, FALSE, NOW(), NOW(), 5, 7, 6),
    ('c3000000-0000-4000-8000-000000000018', 'Rafael',   'c2000000-0000-4000-8000-000000000018', 'c1000000-0000-4000-8000-000000000001', 5.5, 1, FALSE, FALSE, NOW(), NOW(), 6, 5, 5),
    ('c3000000-0000-4000-8000-000000000019', 'Samuel',   'c2000000-0000-4000-8000-000000000019', 'c1000000-0000-4000-8000-000000000001', 5.3, 1, FALSE, FALSE, NOW(), NOW(), 5, 6, 5),
    ('c3000000-0000-4000-8000-000000000020', 'Tiago',    'c2000000-0000-4000-8000-000000000020', 'c1000000-0000-4000-8000-000000000001', 5.0, 1, FALSE, FALSE, NOW(), NOW(), 5, 5, 5),
    ('c3000000-0000-4000-8000-000000000021', 'Convidado A', NULL, 'c1000000-0000-4000-8000-000000000001', 6.5, 1, FALSE, TRUE, NOW(), NULL, NULL, NULL, NULL),
    ('c3000000-0000-4000-8000-000000000022', 'Convidado B', NULL, 'c1000000-0000-4000-8000-000000000001', 7.5, 1, FALSE, TRUE, NOW(), NULL, NULL, NULL, NULL)
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "SkillPoints" = EXCLUDED."SkillPoints",
    "Status" = EXCLUDED."Status",
    "IsGoalkeeper" = EXCLUDED."IsGoalkeeper",
    "IsGuest" = EXCLUDED."IsGuest",
    "AttackRating" = EXCLUDED."AttackRating",
    "DefenseRating" = EXCLUDED."DefenseRating",
    "OverallRating" = EXCLUDED."OverallRating";

INSERT INTO "TeamColors"
    ("Id", "Name", "HexValue", "GroupId", "IsActive", "CreateDate")
VALUES
    ('c4000000-0000-4000-8000-000000000001', 'Azul',     '#1565C0', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW()),
    ('c4000000-0000-4000-8000-000000000002', 'Vermelho', '#D32F2F', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW()),
    ('c4000000-0000-4000-8000-000000000003', 'Verde',    '#2E7D32', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW()),
    ('c4000000-0000-4000-8000-000000000004', 'Amarelo',  '#F9A825', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW()),
    ('c4000000-0000-4000-8000-000000000005', 'Preto',    '#212121', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW()),
    ('c4000000-0000-4000-8000-000000000006', 'Branco',   '#FFFFFF', 'c1000000-0000-4000-8000-000000000001', TRUE, NOW())
ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "HexValue" = EXCLUDED."HexValue",
    "IsActive" = EXCLUDED."IsActive";

COMMIT;

-- Resultado esperado: 1 patota, 20 jogadores vinculados, 2 convidados,
-- 4 goleiros, 2 admins, 1 financeiro e 6 cores ativas.
