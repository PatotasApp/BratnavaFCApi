using BratnavaFC.Domain.Entities;
using FluentAssertions;

namespace BranavaFC.Tests;

public class CalendarEventEntityTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    private static CalendarEventEntity MakeEvent(
        TimeOnly? time = null, bool timeTbd = false) =>
        new(GroupId, "Título", "Descrição", null,
            new DateOnly(2030, 1, 10), time, timeTbd, Guid.NewGuid(), "⚽");

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_ShouldSetAllPropertiesAndTrimStrings()
    {
        // Arrange
        var categoryId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        // Act
        var ev = new CalendarEventEntity(
            GroupId, "  Meu evento  ", "  descrição  ",
            categoryId, new DateOnly(2030, 5, 1), new TimeOnly(18, 30),
            timeTbd: false, createdByUserId: userId, icon: "  🎯  ");

        // Assert
        ev.GroupId.Should().Be(GroupId);
        ev.Title.Should().Be("Meu evento");
        ev.Description.Should().Be("descrição");
        ev.CategoryId.Should().Be(categoryId);
        ev.EventDate.Should().Be(new DateOnly(2030, 5, 1));
        ev.EventTime.Should().Be(new TimeOnly(18, 30));
        ev.TimeTBD.Should().BeFalse();
        ev.CreatedByUserId.Should().Be(userId);
        ev.Icon.Should().Be("🎯");
        ev.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Ctor_WithTimeTbd_ShouldIgnoreProvidedTime()
    {
        // Act — mesmo passando horário, TimeTBD tem prioridade
        var ev = new CalendarEventEntity(
            GroupId, "Evento", null, null,
            new DateOnly(2030, 5, 1), new TimeOnly(10, 0),
            timeTbd: true, createdByUserId: null);

        // Assert
        ev.EventTime.Should().BeNull();
        ev.TimeTBD.Should().BeTrue();
        ev.Description.Should().BeNull();
        ev.Icon.Should().BeNull();
    }

    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        // Act
        var act = () => new CalendarEventEntity(
            Guid.Empty, "Evento", null, null,
            new DateOnly(2030, 1, 1), null, false, null);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*GroupId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WithBlankTitle_ShouldThrow(string title)
    {
        // Act
        var act = () => new CalendarEventEntity(
            GroupId, title, null, null,
            new DateOnly(2030, 1, 1), null, false, null);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*title*");
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [Fact]
    public void Update_WithAllNulls_ShouldKeepEverything()
    {
        // Arrange
        var ev = MakeEvent(time: new TimeOnly(9, 0));

        // Act
        ev.Update(null, null, null, null, null, null);

        // Assert
        ev.Title.Should().Be("Título");
        ev.Description.Should().Be("Descrição");
        ev.EventDate.Should().Be(new DateOnly(2030, 1, 10));
        ev.EventTime.Should().Be(new TimeOnly(9, 0));
        ev.TimeTBD.Should().BeFalse();
        ev.Icon.Should().Be("⚽");
    }

    [Fact]
    public void Update_ShouldApplyNewValues()
    {
        // Arrange
        var ev = MakeEvent(time: new TimeOnly(9, 0));
        var newCategory = Guid.NewGuid();

        // Act
        ev.Update("  Novo título  ", "  nova desc  ", newCategory,
            new DateOnly(2031, 2, 2), new TimeOnly(21, 15), false, "🏆");

        // Assert
        ev.Title.Should().Be("Novo título");
        ev.Description.Should().Be("nova desc");
        ev.CategoryId.Should().Be(newCategory);
        ev.EventDate.Should().Be(new DateOnly(2031, 2, 2));
        ev.EventTime.Should().Be(new TimeOnly(21, 15));
        ev.Icon.Should().Be("🏆");
    }

    [Fact]
    public void Update_WithBlankTitle_ShouldThrow()
    {
        // Arrange
        var ev = MakeEvent();

        // Act
        var act = () => ev.Update("   ", null, null, null, null, null);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Update_WithEmptyIcon_ShouldClearIcon()
    {
        // Arrange — "" limpa, null mantém
        var ev = MakeEvent();

        // Act
        ev.Update(null, null, null, null, null, null, icon: "");

        // Assert
        ev.Icon.Should().BeNull();
    }

    [Fact]
    public void Update_SettingTimeTbdTrue_ShouldClearTime()
    {
        // Arrange
        var ev = MakeEvent(time: new TimeOnly(13, 0));

        // Act
        ev.Update(null, null, null, null, null, timeTbd: true);

        // Assert
        ev.TimeTBD.Should().BeTrue();
        ev.EventTime.Should().BeNull();
    }

    [Fact]
    public void Update_SettingTimeTbdFalse_WithNullTime_ShouldKeepExistingTime()
    {
        // Arrange
        var ev = MakeEvent(time: new TimeOnly(13, 0));

        // Act
        ev.Update(null, null, null, null, eventTime: null, timeTbd: false);

        // Assert
        ev.TimeTBD.Should().BeFalse();
        ev.EventTime.Should().Be(new TimeOnly(13, 0));
    }
}

public class CalendarCategoryEntityTests
{
    private static readonly Guid GroupId = Guid.NewGuid();

    // ── Construtor ────────────────────────────────────────────────────────────

    [Fact]
    public void Ctor_ShouldSetPropertiesAndTrim()
    {
        // Act
        var category = new CalendarCategoryEntity(GroupId, "  Treino  ", "  #ff0000  ", "  ⚽  ");

        // Assert
        category.GroupId.Should().Be(GroupId);
        category.Name.Should().Be("Treino");
        category.Color.Should().Be("#ff0000");
        category.Icon.Should().Be("⚽");
        category.IsSystem.Should().BeFalse();
        category.Id.Should().NotBeEmpty();
    }

    [Fact]
    public void Ctor_WithIsSystemTrue_ShouldMarkAsSystem()
    {
        // Act
        var category = new CalendarCategoryEntity(GroupId, "Sistema", null, null, isSystem: true);

        // Assert
        category.IsSystem.Should().BeTrue();
        category.Color.Should().BeNull();
        category.Icon.Should().BeNull();
    }

    [Fact]
    public void Ctor_WithEmptyGroupId_ShouldThrow()
    {
        // Act
        var act = () => new CalendarCategoryEntity(Guid.Empty, "Nome", null, null);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*GroupId*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_WithBlankName_ShouldThrow(string name)
    {
        // Act
        var act = () => new CalendarCategoryEntity(GroupId, name, null, null);

        // Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*name*");
    }

    // ── Métodos ───────────────────────────────────────────────────────────────

    [Fact]
    public void Rename_ShouldTrimNewName()
    {
        // Arrange
        var category = new CalendarCategoryEntity(GroupId, "Antigo", null, null);

        // Act
        category.Rename("  Novo  ");

        // Assert
        category.Name.Should().Be("Novo");
    }

    [Fact]
    public void Rename_WithBlankName_ShouldThrow()
    {
        // Arrange
        var category = new CalendarCategoryEntity(GroupId, "Antigo", null, null);

        // Act
        var act = () => category.Rename("  ");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void SetColor_ShouldTrimOrClear()
    {
        // Arrange
        var category = new CalendarCategoryEntity(GroupId, "Cat", "#000000", null);

        // Act & Assert
        category.SetColor("  #ffffff  ");
        category.Color.Should().Be("#ffffff");

        category.SetColor(null);
        category.Color.Should().BeNull();
    }

    [Fact]
    public void SetIcon_ShouldTrimOrClear()
    {
        // Arrange
        var category = new CalendarCategoryEntity(GroupId, "Cat", null, "⚽");

        // Act & Assert
        category.SetIcon("  🎯  ");
        category.Icon.Should().Be("🎯");

        category.SetIcon(null);
        category.Icon.Should().BeNull();
    }
}
