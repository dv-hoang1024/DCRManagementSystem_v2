using DCRManagementSystem.Models;

namespace DCRManagementSystem.UserControls;

public interface IWizardStepControl
{
    Task<bool> ValidateStepAsync();
    void BindFromModel(DcrEditModel model);
    void SyncToModel(DcrEditModel model);
}
