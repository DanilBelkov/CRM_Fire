# Technical Plan: Desktop CRM для театральных вещей и реквизита

Документ основан на `tasks/prd-theatre-crm-desktop.md` и предназначен для передачи в разработку. Здесь описаны техническая архитектура, модули, структура проекта, схема данных, задачи реализации и тестирование каждой user story.

## 1. Цель технического документа

Создать понятный план разработки WPF desktop-приложения на .NET/C# с локальной SQLite базой, файловым хранением фотографий и переносимостью на флешке.

Документ должен помочь:

- разбить продукт на технические модули;
- заранее определить границы данных и связей;
- подготовить backlog задач;
- описать тесты для каждой пользовательской истории;
- снизить риск архитектурных переделок на этапе реализации.

## 2. Рекомендуемый стек

- Язык: C#.
- Runtime: .NET 8 LTS.
- UI: WPF.
- Архитектурный паттерн UI: MVVM.
- База данных: SQLite.
- ORM: Entity Framework Core.
- Миграции: EF Core migrations.
- Поиск: SQLite FTS5.
- Excel export/import: ClosedXML.
- Изображения: WPF imaging + генерация thumbnails.
- Тесты:
  - Unit: xUnit.
  - Assertions: FluentAssertions.
  - Mocking: Moq или NSubstitute.
  - Integration: xUnit + временная SQLite база.
  - UI smoke tests: ручной чеклист на MVP, позже FlaUI для автоматизации WPF.
- Логирование: Serilog.
- Packaging: self-contained Windows publish.

## 3. Архитектурный подход

### 3.1 Слои приложения

```text
TheatreCRM.App          WPF UI, Views, Windows, Controls
TheatreCRM.Presentation ViewModels, Commands, UI state
TheatreCRM.Application  Use cases, services, validation, DTO
TheatreCRM.Domain       Entities, enums, domain rules
TheatreCRM.Infrastructure EF Core, SQLite, files, Excel, backups
TheatreCRM.Tests        Unit and integration tests
```

### 3.2 Основные принципы

- UI не должен напрямую работать с EF Core `DbContext`.
- ViewModel вызывает application services.
- Application services работают с repository/unit-of-work или напрямую с абстракциями persistence слоя.
- Domain entities не должны зависеть от WPF.
- Все пути к файлам должны быть относительными к корню приложения или `data`.
- Удаление основных сущностей по умолчанию мягкое через `DeletedAt`.
- Все опасные операции должны быть обратимыми или требовать подтверждения.

### 3.3 Рекомендуемая структура solution

```text
src/
  TheatreCRM.App/
    App.xaml
    MainWindow.xaml
    Views/
    Controls/
    Resources/
    Themes/
  TheatreCRM.Presentation/
    ViewModels/
    Commands/
    Navigation/
    State/
  TheatreCRM.Application/
    Services/
    DTO/
    Validation/
    Search/
    Export/
    Import/
  TheatreCRM.Domain/
    Entities/
    Enums/
    ValueObjects/
  TheatreCRM.Infrastructure/
    Data/
    Migrations/
    Repositories/
    FileStorage/
    Excel/
    Backup/
    Search/
tests/
  TheatreCRM.UnitTests/
  TheatreCRM.IntegrationTests/
tasks/
  prd-theatre-crm-desktop.md
  technical-plan-theatre-crm-desktop.md
```

## 4. Доменная модель

### 4.1 Base entity

```csharp
public abstract class Entity
{
    public Guid Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

### 4.2 CatalogItem

`CatalogItem` - базовая сущность для одежды, реквизита, костюмов и спектаклей.

Поля:

- `Id`
- `Type`
- `Title`
- `Description`
- `MainPhotoId`
- `InventoryNumber`
- `StorageLocation`
- `Condition`
- `ResponsiblePerson`
- `Status`
- `Notes`
- `CreatedAt`
- `UpdatedAt`
- `DeletedAt`

Рекомендуемая реализация: table-per-hierarchy через одну таблицу `CatalogItems` и отдельные detail-таблицы для специфичных полей. Это упростит общий поиск, теги, фото, историю и мягкое удаление.

### 4.3 Entity types

```csharp
public enum CatalogItemType
{
    Clothing = 1,
    Prop = 2,
    Costume = 3,
    Performance = 4
}
```

### 4.4 Status and condition

```csharp
public enum CatalogItemStatus
{
    Active = 1,
    Archived = 2,
    WrittenOff = 3
}

public enum ItemCondition
{
    New = 1,
    Good = 2,
    NeedsRepair = 3,
    WrittenOff = 4,
    Other = 99
}
```

## 5. Схема базы данных

### 5.1 Core tables

#### CatalogItems

| Column | Type | Notes |
|---|---|---|
| Id | TEXT GUID PK | Уникальный ID |
| Type | INTEGER | Clothing/Prop/Costume/Performance |
| Title | TEXT NOT NULL | Название |
| Description | TEXT NULL | Описание |
| MainPhotoId | TEXT NULL | FK Photos |
| InventoryNumber | TEXT NULL | Индекс, не обязательно unique |
| StorageLocation | TEXT NULL | Место хранения |
| Condition | INTEGER | Состояние |
| ResponsiblePerson | TEXT NULL | Ответственный |
| Status | INTEGER | Active/Archived/WrittenOff |
| Notes | TEXT NULL | Заметки |
| CreatedAt | TEXT | UTC |
| UpdatedAt | TEXT | UTC |
| DeletedAt | TEXT NULL | Мягкое удаление |

#### ClothingDetails

| Column | Type |
|---|---|
| CatalogItemId | TEXT PK/FK |
| Size | TEXT NULL |
| GenderPresentation | TEXT NULL |
| Color | TEXT NULL |
| Material | TEXT NULL |
| Measurements | TEXT NULL |
| CareInstructions | TEXT NULL |

#### PropDetails

| Column | Type |
|---|---|
| CatalogItemId | TEXT PK/FK |
| Dimensions | TEXT NULL |
| Weight | TEXT NULL |
| Material | TEXT NULL |
| Fragility | INTEGER |
| UsageNotes | TEXT NULL |

#### CostumeDetails

| Column | Type |
|---|---|
| CatalogItemId | TEXT PK/FK |
| CharacterName | TEXT NULL |
| ActorName | TEXT NULL |
| SceneNotes | TEXT NULL |

#### PerformanceDetails

| Column | Type |
|---|---|
| CatalogItemId | TEXT PK/FK |
| Director | TEXT NULL |
| PremiereDate | TEXT NULL |
| Season | TEXT NULL |

### 5.2 Tag tables

#### TagGroups

| Column | Type |
|---|---|
| Id | TEXT GUID PK |
| Name | TEXT NOT NULL |
| Description | TEXT NULL |
| Color | TEXT NULL |
| SortOrder | INTEGER |
| IsRequired | INTEGER |
| AllowedEntityTypes | TEXT JSON |
| CreatedAt | TEXT |
| UpdatedAt | TEXT |

#### Tags

| Column | Type |
|---|---|
| Id | TEXT GUID PK |
| GroupId | TEXT FK |
| Name | TEXT NOT NULL |
| Description | TEXT NULL |
| Color | TEXT NULL |
| Icon | TEXT NULL |
| SortOrder | INTEGER |
| IsArchived | INTEGER |
| CreatedAt | TEXT |
| UpdatedAt | TEXT |

#### CatalogItemTags

| Column | Type |
|---|---|
| CatalogItemId | TEXT FK |
| TagId | TEXT FK |

Composite primary key: `CatalogItemId + TagId`.

### 5.3 Relationship tables

- `CostumeClothingItems`: `CostumeId`, `ClothingItemId`.
- `CostumeProps`: `CostumeId`, `PropId`.
- `PerformanceCostumes`: `PerformanceId`, `CostumeId`.
- `PerformanceProps`: `PerformanceId`, `PropId`.
- `PerformanceClothingItems`: `PerformanceId`, `ClothingItemId`.

Все таблицы связей должны иметь composite primary key и индексы на обе колонки.

### 5.4 Media tables

#### Photos

| Column | Type |
|---|---|
| Id | TEXT GUID PK |
| CatalogItemId | TEXT FK |
| RelativePath | TEXT NOT NULL |
| ThumbnailRelativePath | TEXT NULL |
| OriginalFileName | TEXT NULL |
| SortOrder | INTEGER |
| CreatedAt | TEXT |

### 5.5 Settings and system tables

- `SavedViews`
- `AuditLog`
- `BackupLog`
- `AppSettings`

### 5.6 Search tables

SQLite FTS5 virtual table:

```sql
CREATE VIRTUAL TABLE CatalogItemsFts USING fts5(
    CatalogItemId UNINDEXED,
    Type UNINDEXED,
    Title,
    Description,
    InventoryNumber,
    Notes
);
```

FTS индекс должен обновляться при создании, редактировании, мягком удалении и восстановлении сущности.

## 6. Application services

### 6.1 Catalog services

- `ClothingService`
- `PropService`
- `CostumeService`
- `PerformanceService`
- `CatalogItemService`

Ответственность:

- создание;
- редактирование;
- мягкое удаление;
- восстановление;
- получение карточки;
- получение списка с фильтрами;
- работа со связями;
- вызов audit log;
- обновление FTS индекса.

### 6.2 Tag services

- `TagService`
- `TagGroupService`

Ответственность:

- CRUD тегов и групп;
- архивирование тега;
- замена тега другим тегом;
- снятие тега со связанных сущностей;
- подсчет связанных сущностей перед опасной операцией;
- применение тегов к сущности.

### 6.3 Search services

- `FullTextSearchService`
- `SemanticSearchService` как optional interface.

MVP должен использовать `FullTextSearchService` на SQLite FTS5.

### 6.4 View configuration services

- `SavedViewService`
- `UserWorkspaceService`

Ответственность:

- сохранение пользовательских вкладок;
- сохранение фильтров;
- сохранение видимых колонок;
- сохранение группировок;
- восстановление состояния интерфейса при запуске.

### 6.5 File and photo services

- `PhotoStorageService`
- `ThumbnailService`
- `RelativePathService`

Ответственность:

- копирование фото в `data/photos`;
- генерация уникального имени файла;
- создание thumbnail;
- удаление неиспользуемых файлов по отдельной maintenance-команде;
- запрет абсолютных путей в базе.

### 6.6 Import/export services

- `ExcelExportService`
- `ExcelImportService`
- `CsvImportService`

### 6.7 Backup services

- `BackupService`
- `RestoreService`

## 7. UI structure

### 7.1 MainWindow

Состав:

- левая боковая панель;
- верхняя строка текущего раздела;
- search bar;
- панель фильтров;
- список/таблица/карточки;
- detail panel или отдельное окно карточки;
- status bar для ошибок, экспорта, импорта и backup.

### 7.2 Sections

Базовые разделы:

- Одежда;
- Реквизит;
- Костюмы;
- Спектакли;
- Теги;
- Корзина;
- Настройки.

Пользовательские разделы:

- saved views с `IsShownInSidebar = true`.

### 7.3 ViewModels

- `MainWindowViewModel`
- `SidebarViewModel`
- `CatalogListViewModel`
- `CatalogDetailsViewModel`
- `ClothingEditorViewModel`
- `PropEditorViewModel`
- `CostumeEditorViewModel`
- `PerformanceEditorViewModel`
- `TagManagerViewModel`
- `SavedViewEditorViewModel`
- `ExportViewModel`
- `ImportViewModel`
- `BackupViewModel`
- `TrashViewModel`

## 8. Development Milestones

### M0: Project foundation

- Создать solution и проекты.
- Настроить WPF MVVM.
- Настроить DI container.
- Подключить Serilog.
- Настроить EF Core SQLite.
- Создать базовые миграции.
- Создать тестовые проекты.
- Настроить build pipeline локально через `dotnet build` и `dotnet test`.

### M1: Core data model

- Реализовать `CatalogItem`.
- Реализовать detail-таблицы.
- Реализовать теги, группы тегов и связи.
- Реализовать фото.
- Реализовать связи между сущностями.
- Написать integration tests для схемы БД.

### M2: CRUD and relationships

- Реализовать CRUD одежды.
- Реализовать CRUD реквизита.
- Реализовать CRUD костюмов.
- Реализовать CRUD спектаклей.
- Реализовать редакторы связей.
- Реализовать карточки деталей.

### M3: Tags, filters, saved views

- Реализовать tag manager.
- Реализовать безопасное удаление тегов.
- Реализовать фильтры по тегам.
- Реализовать группировку по группе тегов.
- Реализовать saved views и пользовательские вкладки.

### M4: Search, export, import

- Реализовать FTS5.
- Реализовать поиск внутри разделов.
- Реализовать Excel export.
- Реализовать Excel import/CSV import.

### M5: Safety, history, backup

- Реализовать audit log.
- Реализовать корзину.
- Реализовать backup/restore.
- Реализовать автоматический backup.

### M6: UX polish and release packaging

- Довести темную тему.
- Добавить зеленые акценты.
- Проверить компактность списков.
- Добавить empty/error/loading states.
- Собрать self-contained publish.
- Протестировать запуск с флешки или внешней папки.

## 9. Detailed Task Breakdown by User Story

### US-001: Создание базовой структуры локального приложения

Development tasks:

- Создать WPF проект `TheatreCRM.App`.
- Создать layered solution.
- Добавить `AppPathService`, который определяет корень приложения и папку `data`.
- При запуске создавать `data`, `data/photos`, `data/backups`, `data/exports`, `data/settings`.
- Подключить SQLite файл `data/theatre-crm.sqlite`.
- Добавить EF Core миграции.
- Добавить startup migration runner.
- Добавить обработку ошибки блокировки или недоступности базы.

Tests:

- Unit: `AppPathService` возвращает корректные относительные пути.
- Unit: `AppPathService` не формирует абсолютные пути для хранения в базе.
- Integration: при первом запуске создаются нужные папки.
- Integration: при первом запуске создается SQLite база.
- Integration: повторный запуск не удаляет существующие данные.
- Manual: скопировать папку приложения в другую директорию и проверить, что база открывается.

### US-002: CRUD для одежды

Development tasks:

- Создать `ClothingDetails` entity.
- Создать `ClothingService`.
- Создать список одежды.
- Создать форму создания/редактирования одежды.
- Добавить поля: title, description, inventory number, storage, condition, responsible, size, color, material, measurements, care instructions.
- Добавить назначение тегов.
- Добавить добавление фото.
- Добавить отображение связанных костюмов и спектаклей.
- Добавить мягкое удаление.

Tests:

- Unit: нельзя создать одежду без названия.
- Unit: обновление одежды меняет `UpdatedAt`.
- Integration: одежда сохраняется в `CatalogItems` и `ClothingDetails`.
- Integration: назначенные теги сохраняются в `CatalogItemTags`.
- Integration: мягкое удаление заполняет `DeletedAt`.
- Integration: удаленная одежда не отображается в обычном списке.
- UI manual: создать одежду с фото и тегами.
- UI manual: открыть карточку и перейти по ссылке на связанный костюм.

### US-003: CRUD для реквизита

Development tasks:

- Создать `PropDetails` entity.
- Создать `PropService`.
- Создать список реквизита.
- Создать форму создания/редактирования реквизита.
- Добавить поля: dimensions, weight, material, fragility, usage notes.
- Добавить назначение тегов и фото.
- Добавить отображение связанных костюмов и спектаклей.
- Добавить мягкое удаление.

Tests:

- Unit: нельзя создать реквизит без названия.
- Unit: fragility принимает только допустимые значения enum.
- Integration: реквизит сохраняется в `CatalogItems` и `PropDetails`.
- Integration: связи с костюмами читаются корректно.
- Integration: связи со спектаклями напрямую читаются корректно.
- Integration: мягко удаленный реквизит исключается из списка.
- UI manual: создать реквизит, назначить теги, добавить фото.
- UI manual: открыть реквизит из карточки спектакля.

### US-004: CRUD для костюмов

Development tasks:

- Создать `CostumeDetails` entity.
- Создать `CostumeService`.
- Создать список костюмов.
- Создать форму создания/редактирования костюма.
- Добавить поля: character name, actor name, scene notes.
- Добавить selector одежды.
- Добавить selector реквизита.
- Добавить связи `CostumeClothingItems` и `CostumeProps`.
- Добавить отображение связанных спектаклей.

Tests:

- Unit: нельзя создать костюм без названия.
- Unit: повторное добавление одной одежды в костюм не создает дубль.
- Unit: повторное добавление одного реквизита в костюм не создает дубль.
- Integration: костюм сохраняется в `CatalogItems` и `CostumeDetails`.
- Integration: костюм может содержать несколько элементов одежды.
- Integration: одна одежда может входить в несколько костюмов.
- Integration: один реквизит может входить в несколько костюмов.
- UI manual: создать костюм и добавить в него одежду и реквизит.
- UI manual: открыть связанный спектакль из карточки костюма.

### US-005: CRUD для спектаклей

Development tasks:

- Создать `PerformanceDetails` entity.
- Создать `PerformanceService`.
- Создать список спектаклей.
- Создать форму создания/редактирования спектакля.
- Добавить поля: director, premiere date, season.
- Добавить selector костюмов.
- Добавить selector прямого реквизита.
- Добавить selector прямой одежды.
- Добавить связи `PerformanceCostumes`, `PerformanceProps`, `PerformanceClothingItems`.
- Добавить группированное отображение состава спектакля.

Tests:

- Unit: нельзя создать спектакль без названия.
- Unit: повторное добавление костюма в спектакль не создает дубль.
- Integration: спектакль сохраняется в `CatalogItems` и `PerformanceDetails`.
- Integration: один костюм может входить в несколько спектаклей.
- Integration: спектакль может иметь прямой реквизит.
- Integration: спектакль может иметь прямую одежду.
- UI manual: создать спектакль и добавить костюмы, реквизит и одежду.
- UI manual: проверить, что состав спектакля разделен на три группы.

### US-006: Управление тегами и группами тегов

Development tasks:

- Создать `TagGroup` entity.
- Создать `Tag` entity.
- Создать `TagService` и `TagGroupService`.
- Создать экран управления тегами.
- Добавить создание, редактирование и сортировку групп тегов.
- Добавить создание, редактирование и сортировку тегов.
- Добавить выбор цвета.
- Добавить ограничение применимости группы тегов по типам сущностей.

Tests:

- Unit: нельзя создать тег без имени.
- Unit: нельзя создать группу тегов без имени.
- Unit: переименование тега не меняет `Id`.
- Integration: тег сохраняется внутри группы.
- Integration: перемещение тега в другую группу сохраняет связи с сущностями.
- Integration: один тег назначается нескольким сущностям.
- UI manual: создать группу тегов и несколько тегов.
- UI manual: назначить теги одежде, реквизиту, костюму и спектаклю.

### US-007: Безопасное удаление и архивирование тегов

Development tasks:

- Добавить метод подсчета связанных сущностей для тега.
- Добавить dialog удаления тега.
- Реализовать архивирование тега.
- Реализовать замену тега другим тегом.
- Реализовать снятие тега со всех сущностей.
- Добавить запись в audit log.
- Скрывать архивные теги из обычного autocomplete.

Tests:

- Unit: удаление тега не удаляет `CatalogItem`.
- Unit: архивирование выставляет `IsArchived`.
- Unit: замена тега переносит связи на новый тег.
- Integration: при снятии тега удаляются только записи `CatalogItemTags`.
- Integration: архивный тег остается видимым в старой карточке.
- Integration: audit log фиксирует операцию.
- UI manual: удалить связанный тег и выбрать замену.
- UI manual: проверить, что карточки остались на месте.

### US-008: Настройка вкладок, фильтров и панелей

Development tasks:

- Создать entity `SavedView`.
- Создать `SavedViewService`.
- Создать editor пользовательского представления.
- Добавить сохранение `SectionType`, `ViewType`, `PinnedTagGroupIds`, `DefaultTagFilters`, `VisibleFields`, `SortMode`, `GroupByTagGroupId`.
- Добавить отображение saved views в sidebar.
- Добавить применение saved view к списку.
- Добавить сохранение последнего активного раздела.

Tests:

- Unit: saved view нельзя создать без имени.
- Unit: saved view должен ссылаться на допустимый section type.
- Integration: saved view сохраняется и читается после перезапуска контекста.
- Integration: фильтры saved view применяются к запросу списка.
- Integration: `IsShownInSidebar` добавляет представление в sidebar model.
- UI manual: создать вкладку "Зимние вещи" на основе тега.
- UI manual: перезапустить приложение и проверить, что вкладка осталась.

### US-009: Группировка по тегам

Development tasks:

- Добавить `CatalogQuery`.
- Добавить параметры `TagGroupId`, `TagId`, `SearchText`, `Sort`.
- Реализовать группировку по группе тегов.
- Добавить группу "Без тега".
- Добавить UI selector группы тегов.
- Добавить UI selector конкретного тега.
- Объединить группировку с поиском.

Tests:

- Unit: сущность без тега попадает в группу "Без тега".
- Unit: выбор конкретного тега фильтрует список.
- Integration: группировка возвращает группы по всем тегам выбранной группы.
- Integration: поиск применяется внутри выбранного фильтра.
- Integration: группировка работает отдельно для каждого раздела.
- UI manual: выбрать группу "Сезон" и проверить группы по тегам.
- UI manual: выбрать тег "Зима" и проверить, что видны только зимние элементы.

### US-010: Поиск по названию и описанию

Development tasks:

- Включить FTS5 в SQLite.
- Создать `CatalogItemsFts`.
- Реализовать sync FTS при create/update/delete/restore.
- Создать `FullTextSearchService`.
- Подключить поиск к каждому разделу.
- Добавить debounce ввода в UI.
- Добавить empty state.

Tests:

- Unit: пустой поисковый запрос возвращает обычный список.
- Unit: поисковый запрос нормализуется.
- Integration: поиск находит по названию.
- Integration: поиск находит по описанию.
- Integration: поиск не возвращает сущности другого раздела.
- Integration: мягко удаленные сущности не возвращаются.
- Integration: обновление описания обновляет FTS индекс.
- UI manual: найти карточку по слову из описания.

### US-011: Расширенный семантический поиск

Development tasks:

- Определить interface `ISemanticSearchService`.
- Реализовать feature flag для семантического поиска.
- Подготовить placeholder UI режима поиска.
- Для MVP оставить выключенным по умолчанию.
- В полной версии добавить локальную ONNX модель или другой локальный механизм.
- Добавить fallback на FTS5.

Tests:

- Unit: при выключенном feature flag используется FTS5.
- Unit: при ошибке semantic service выполняется fallback на FTS5.
- Integration: отсутствие локальной модели не ломает обычный поиск.
- UI manual: проверить, что пользователь видит текущий режим поиска.
- UI manual: проверить, что обычный поиск работает без семантической модели.

### US-012: Фото и галерея

Development tasks:

- Создать `Photo` entity.
- Создать `PhotoStorageService`.
- Создать `ThumbnailService`.
- Реализовать копирование фото в `data/photos/{entityType}`.
- Реализовать генерацию thumbnail.
- Реализовать выбор main photo.
- Реализовать удаление фото из карточки.
- Добавить photo gallery control.

Tests:

- Unit: `PhotoStorageService` генерирует относительный путь.
- Unit: main photo можно заменить.
- Unit: удаление main photo назначает новую main photo или очищает поле.
- Integration: фото сохраняется в таблице `Photos`.
- Integration: файл копируется в ожидаемую папку.
- Integration: после переноса корня приложения относительный путь остается рабочим.
- UI manual: добавить несколько фото и выбрать обложку.
- UI manual: удалить фото и проверить карточку.

### US-013: Экспорт в Excel

Development tasks:

- Создать `ExcelExportService`.
- Определить DTO экспорта для каждой сущности.
- Создать листы: одежда, реквизит, костюмы, спектакли, теги, связи.
- Реализовать export без фото.
- Реализовать export с фото или ссылками на фото.
- Добавить progress dialog.
- Сохранять экспорт в `data/exports`.

Tests:

- Unit: export DTO корректно собирает теги.
- Unit: export DTO корректно собирает связи.
- Integration: экспорт создает `.xlsx`.
- Integration: `.xlsx` содержит обязательные листы.
- Integration: export без фото не добавляет изображения.
- Integration: export с фото добавляет изображения или ссылки согласно режиму.
- UI manual: экспортировать базу и открыть файл в Excel/LibreOffice.

### US-014: Импорт из Excel/CSV

Development tasks:

- Создать `ExcelImportService`.
- Создать `CsvImportService`.
- Создать экран выбора файла.
- Реализовать preview rows.
- Реализовать mapping колонок.
- Реализовать validation перед импортом.
- Реализовать duplicate detection по inventory number и названию.
- Реализовать import report.

Tests:

- Unit: parser читает CSV с заголовками.
- Unit: mapping колонок применяется к DTO.
- Unit: строка без названия считается ошибочной.
- Unit: duplicate detection находит совпадение по inventory number.
- Integration: валидный Excel импортирует одежду.
- Integration: ошибочные строки не записываются в базу.
- UI manual: импортировать тестовый файл и проверить preview.
- UI manual: импортировать файл с ошибкой и проверить отчет.

### US-015: История изменений

Development tasks:

- Создать `AuditLog` entity.
- Создать `AuditService`.
- Добавить operator name в настройки.
- Фиксировать create/update/delete/restore.
- Фиксировать изменение тегов.
- Фиксировать изменение связей.
- Добавить tab истории в карточке.

Tests:

- Unit: audit entry содержит entity id, operation type и timestamp.
- Unit: изменение поля формирует old/new values.
- Integration: создание карточки пишет audit log.
- Integration: изменение тегов пишет audit log.
- Integration: изменение связей пишет audit log.
- UI manual: изменить карточку и увидеть запись в истории.

### US-016: Резервное копирование и восстановление

Development tasks:

- Создать `BackupService`.
- Создать `RestoreService`.
- Реализовать zip архивацию папки `data`.
- Добавить ручной backup.
- Добавить настройку auto backup.
- Добавить restore flow.
- Перед restore создавать safety backup.
- Логировать backup/restore операции.

Tests:

- Unit: backup file name содержит дату и время.
- Unit: backup service включает базу, фото и настройки.
- Integration: ручной backup создает zip.
- Integration: restore восстанавливает SQLite файл.
- Integration: restore восстанавливает фото.
- Integration: перед restore создается safety backup.
- UI manual: создать backup, удалить тестовую карточку, восстановить backup.

### US-017: Корзина и мягкое удаление

Development tasks:

- Добавить global query filters для `DeletedAt`.
- Создать `TrashService`.
- Создать экран корзины.
- Реализовать restore.
- Реализовать permanent delete.
- Перед permanent delete показывать связи.
- Очистка связанных join-записей только при permanent delete.

Tests:

- Unit: soft delete выставляет `DeletedAt`.
- Unit: restore очищает `DeletedAt`.
- Integration: soft deleted item не виден в обычном списке.
- Integration: soft deleted item виден в корзине.
- Integration: restore возвращает item в обычный список.
- Integration: permanent delete удаляет item и его join-связи.
- UI manual: удалить карточку, восстановить ее из корзины.

### US-018: Современный интерфейс

Development tasks:

- Создать dark theme resource dictionary.
- Определить палитру: background, surface, border, text, muted text, green accent, danger.
- Создать стили кнопок со скруглениями.
- Создать common controls: search box, tag pill, entity card, empty state, confirmation dialog.
- Реализовать sidebar layout.
- Реализовать responsive resizing для основных панелей.
- Добавить иконки.
- Добавить hover/focus states.

Tests:

- Unit: не требуется, кроме ViewModel state tests.
- ViewModel test: sidebar содержит базовые разделы.
- ViewModel test: active section меняется при выборе пункта меню.
- UI manual: проверить темную тему на всех основных экранах.
- UI manual: проверить, что кнопки и поля не перекрываются при маленьком окне.
- UI manual: проверить, что зеленый акцент используется для основных действий.
- UI manual: проверить keyboard navigation для основных форм.

## 10. Cross-Cutting Tests

### 10.1 Database tests

- Миграции применяются к пустой базе.
- Миграции повторно не ломают существующую базу.
- Все foreign keys включены и работают.
- Все many-to-many связи имеют composite primary key.
- Мягкое удаление не удаляет join-записи до permanent delete.

### 10.2 File portability tests

- База не содержит абсолютных путей к фото.
- После переноса папки приложения фото открываются.
- Export path и backup path создаются относительно `data`.
- Приложение корректно сообщает об ошибке, если флешка стала read-only.

### 10.3 Performance tests

- Seed 10 000 catalog items.
- Поиск FTS возвращает результат менее чем за 500 мс на dev-машине.
- Открытие списка не загружает все фото в полном размере.
- UI virtualization включена для больших списков.
- Экспорт 10 000 строк завершается без out-of-memory.

### 10.4 Regression tests

- Переименование тега не ломает фильтры и saved views.
- Архивирование тега не удаляет его из старых карточек.
- Изменение связи костюм-спектакль отражается в обеих карточках.
- Удаление фото не ломает карточку.
- Восстановление из корзины возвращает связи.

## 11. Definition of Done

Для каждой story:

- Реализованы domain/entity изменения.
- Реализованы application services.
- Реализован UI flow.
- Добавлены unit tests для бизнес-логики.
- Добавлены integration tests для БД и связей.
- Пройден ручной UI checklist.
- Нет абсолютных путей в базе.
- Ошибки показываются пользователю понятным текстом.
- `dotnet build` проходит без ошибок.
- `dotnet test` проходит без ошибок.

Для релиза:

- Приложение собирается как self-contained package.
- Приложение запускается из новой папки без установки сервера.
- Приложение работает после переноса папки.
- Backup/restore проверены вручную.
- Export Excel проверен вручную.
- На тестовой базе нет критичных тормозов UI.

## 12. Suggested Implementation Order

1. Foundation: solution, WPF shell, SQLite, migrations, DI.
2. Domain model: catalog items, details, tags, photos, relationships.
3. Basic CRUD: одежда и реквизит.
4. Composite CRUD: костюмы и спектакли.
5. Tags and filters: tag manager, grouping, saved views.
6. Search: FTS5 and per-section search.
7. Photos: gallery, thumbnails, relative paths.
8. Export: Excel without photos, then with photos.
9. Safety: trash, audit log, backup/restore.
10. Import: Excel/CSV.
11. UI polish and packaging.
12. Optional semantic search.

## 13. Key Technical Risks

- WPF списки могут тормозить без virtualization.
- Фото большого размера могут перегружать память, если открывать оригиналы в списках.
- SQLite файл на флешке может быть медленнее, чем на SSD.
- Одновременное открытие приложения на двух компьютерах с одной флешки может повредить ожидания пользователя, поэтому нужно показывать блокировку.
- Семантический поиск может усложнить portable packaging, поэтому он должен быть optional.

## 14. Initial Engineering Checklist

- [ ] Создать solution.
- [ ] Добавить проекты по слоям.
- [ ] Настроить WPF MVVM.
- [ ] Настроить DI.
- [ ] Настроить EF Core SQLite.
- [ ] Добавить миграцию initial schema.
- [ ] Добавить xUnit test projects.
- [ ] Добавить seed data для разработки.
- [ ] Добавить dark theme.
- [ ] Реализовать первый vertical slice: создание одежды с тегом и фото.

