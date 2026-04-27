using Hex.Scaffold.Domain.AccountAggregate;
using Hex.Scaffold.Domain.AccountAggregate.Events;

namespace Hex.Scaffold.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class AccountAggregateTests
{
  private static Account NewAccount(IEnumerable<AppliedConfiguration>? configs = null) =>
    Account.Create(
      livemode: false,
      displayName: "Test",
      contactEmail: "test@example.com",
      contactPhone: null,
      appliedConfigurations: configs ?? [AppliedConfiguration.Customer],
      configurationJson: null,
      identityJson: null,
      defaultsJson: null,
      metadataJson: null);

  [Fact]
  public void Create_GeneratesAcctPrefixedId_AndEmitsCreatedEvent()
  {
    var a = NewAccount();

    a.Id.Value.ShouldStartWith("acct_");
    a.Created.ShouldBeGreaterThan(DateTime.UtcNow.AddSeconds(-5));
    a.DomainEvents.Count.ShouldBe(1);
    a.DomainEvents.First().ShouldBeOfType<AccountCreatedEvent>();
  }

  [Fact]
  public void Create_WithMerchantConfig_DerivesFullDashboard()
  {
    var a = NewAccount([AppliedConfiguration.Customer, AppliedConfiguration.Merchant]);

    a.Dashboard.ShouldBe("full");
    a.AppliedConfigurations.ShouldBe(["customer", "merchant"], ignoreOrder: true);
  }

  [Fact]
  public void Create_WithRecipientOnly_DerivesExpressDashboard()
  {
    var a = NewAccount([AppliedConfiguration.Recipient]);

    a.Dashboard.ShouldBe("express");
  }

  [Fact]
  public void ApplyUpdate_OmittedFields_AreLeftAlone()
  {
    var a = NewAccount();
    a.ClearDomainEvents();
    var originalEmail = a.ContactEmail;

    a.ApplyUpdate(
      displayName: (true, "New Display"),
      contactEmail: (false, null),       // omitted — should not change
      contactPhone: (false, null),
      appliedConfigurations: (false, null),
      configurationJson: (false, null),
      identityJson: (false, null),
      defaultsJson: (false, null),
      metadataJson: (false, null));

    a.DisplayName.ShouldBe("New Display");
    a.ContactEmail.ShouldBe(originalEmail);
    a.DomainEvents.Count.ShouldBe(1);
    a.DomainEvents.First().ShouldBeOfType<AccountUpdatedEvent>();
  }

  [Fact]
  public void ApplyUpdate_ExplicitNull_ClearsField()
  {
    var a = NewAccount();
    a.ClearDomainEvents();

    a.ApplyUpdate(
      displayName: (true, null),         // explicit null — should clear
      contactEmail: (false, null),
      contactPhone: (false, null),
      appliedConfigurations: (false, null),
      configurationJson: (false, null),
      identityJson: (false, null),
      defaultsJson: (false, null),
      metadataJson: (false, null));

    a.DisplayName.ShouldBeNull();
  }

  [Fact]
  public void ApplyUpdate_NoChange_DoesNotEmitEvent()
  {
    var a = NewAccount();
    a.ClearDomainEvents();

    // Same display name as ctor → not actually a change.
    a.ApplyUpdate(
      displayName: (true, "Test"),
      contactEmail: (false, null),
      contactPhone: (false, null),
      appliedConfigurations: (false, null),
      configurationJson: (false, null),
      identityJson: (false, null),
      defaultsJson: (false, null),
      metadataJson: (false, null));

    a.DomainEvents.ShouldBeEmpty();
  }

  [Fact]
  public void Close_SetsClosedAndEmitsEvent()
  {
    var a = NewAccount();
    a.ClearDomainEvents();

    a.Close();

    a.Closed.ShouldBeTrue();
    a.DomainEvents.Count.ShouldBe(1);
    a.DomainEvents.First().ShouldBeOfType<AccountUpdatedEvent>();
  }
}
