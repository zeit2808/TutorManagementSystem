using TutorManagement.SharedKernel.Primitives;

namespace TutorManagement.Modules.Organizations.Application.Centers;

public static class CenterErrors
{
    public static readonly Error NotFound = new("center_not_found", "The center was not found.");
    public static readonly Error TaxCodeAlreadyExists = new(
        "center_tax_code_already_exists",
        "A center with this tax code already exists.",
        "taxCode");
    public static readonly Error InvalidStatusTransition = new(
        "invalid_center_status_transition",
        "The requested center status transition is not allowed.");
    public static readonly Error ChangeAlreadyPending = new(
        "center_change_already_pending", "A center change request is already pending.");
    public static readonly Error ChangeNotFound = new(
        "center_change_not_found", "The center change request was not found.");
    public static readonly Error ChangeAlreadyReviewed = new(
        "center_change_already_reviewed", "The center change request was already reviewed.");
    public static readonly Error CenterDetailsChanged = new(
        "center_details_changed", "The center changed since this request was submitted.");
    public static readonly Error ResubmitRequiresRejection = new(
        "center_not_rejected", "Only a rejected center can be corrected and resubmitted.");
    public static readonly Error DirectoryRequiresApproval = new(
        "center_not_approved_for_directory", "Only an approved center can be listed in the directory.");
    public static readonly Error ChangeNotPending = new(
        "center_change_not_pending", "Only a pending change request can be withdrawn.");
    public static readonly Error ChangeHasNoDifferences = new(
        "center_change_has_no_differences", "The proposed profile matches the current profile.");
}
