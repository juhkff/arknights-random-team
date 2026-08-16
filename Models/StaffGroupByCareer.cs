namespace arknights_random_team.Models;

public class StaffGroupByCareer
{
    public Career Career { get; set; }
    public List<Staff> StaffList { get; set; } = [];
}
